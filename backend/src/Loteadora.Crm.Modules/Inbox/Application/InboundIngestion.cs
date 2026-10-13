using System.Text.Json;
using Loteadora.Crm.Modules.Audit.Domain;
using Loteadora.Crm.Modules.Crm.Domain;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Inbox.Domain;
using Loteadora.Crm.Modules.Inbox.Providers;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.Modules.Pipeline.Domain;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loteadora.Crm.Modules.Inbox.Application;

public sealed record IngestResult(Guid ConversationId, Guid MessageId, Guid LeadId, bool Duplicate, bool LeadCreated);

/// <summary>
/// Entrada de mensagens do cliente (webhook da Meta ou simulador) — MESMO caminho para os dois.
/// Executa no tenant do canal, como sistema. Etapas:
///  1. idempotência pelo id externo (wamid);
///  2. contato pelo telefone → lead aberto mais recente (ou cria contato + lead novo no funil);
///  3. conversa do lead naquele canal (ou cria);
///  4. grava a mensagem, atualiza a conversa e o último contato do lead;
///  5. evento MessageReceived no outbox (notificações, automações, Copilot).
/// Um lock por (organização, telefone) evita leads duplicados com webhooks simultâneos.
/// </summary>
public sealed class InboundIngestion(AppDbContext db, TimeProvider clock)
{
    public async Task<IngestResult> IngestAsync(Channel channel, string fromE164, string? profileName, string externalId, MessageType type, string body, DateTimeOffset at, CancellationToken ct)
    {
        var phone = PhoneNumber.NormalizeE164(fromE164);
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({channel.OrganizationId.ToString() + phone}, 0))", ct);

        var existing = await db.Messages.Where(m => m.ExternalId == externalId)
            .Select(m => new { m.Id, m.ConversationId }).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            var leadOfExisting = await db.Conversations.Where(c => c.Id == existing.ConversationId).Select(c => c.LeadId).FirstAsync(ct);
            return new IngestResult(existing.ConversationId, existing.Id, leadOfExisting, Duplicate: true, LeadCreated: false);
        }

        var now = clock.GetUtcNow();
        var leadCreated = false;
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.PhoneE164 == phone && c.AnonymizedAt == null, ct);
        Lead? lead = null;
        if (contact is not null)
        {
            lead = await db.Leads.Where(l => l.ContactId == contact.Id && l.Status == LeadStatus.Open)
                .OrderByDescending(l => l.EnteredAt).FirstOrDefaultAsync(ct);
        }
        else
        {
            var name = string.IsNullOrWhiteSpace(profileName) ? $"Contato WhatsApp {phone}" : profileName.Trim();
            contact = new Contact(channel.OrganizationId, Guard.MaxLength(name, 200, "Nome")!, phone, null);
            db.Contacts.Add(contact);
        }

        if (lead is null)
        {
            var stage = await EntryStageAsync(ct);
            var owner = await ResolveOwnerAsync(channel, ct);
            lead = new Lead(channel.OrganizationId, contact.Id, owner, stage, "WhatsApp", null, now);
            db.Leads.Add(lead);
            leadCreated = true;
            Audit(channel.OrganizationId, now, "lead.created", "lead", lead.Id, new { source = "WhatsApp", channelId = channel.Id, ownerUserId = owner, stageId = stage.Id });
        }

        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.LeadId == lead.Id && c.ChannelId == channel.Id, ct);
        if (conversation is null)
        {
            conversation = new Conversation(channel.OrganizationId, channel.Id, lead.Id, contact.Id, phone, lead.OwnerUserId);
            db.Conversations.Add(conversation);
        }

        var message = Message.Inbound(channel.OrganizationId, conversation.Id, externalId, type, body, at);
        db.Messages.Add(message);
        conversation.RegisterInbound(message);
        lead.RegisterContact(at);

        // Auditoria sem o conteúdo da mensagem (dado pessoal).
        Audit(channel.OrganizationId, now, "message.received", "lead", lead.Id, new { conversationId = conversation.Id, messageId = message.Id, type = type.ToString(), channelId = channel.Id });

        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);

        return new IngestResult(conversation.Id, message.Id, lead.Id, Duplicate: false, leadCreated);
    }

    public async Task ApplyStatusAsync(StatusEvent status, CancellationToken ct)
    {
        var message = await db.Messages.FirstOrDefaultAsync(m => m.ExternalId == status.ExternalId, ct);
        if (message is null)
            return; // mensagem enviada fora do CRM ou ainda não registrada: ignorar
        message.ApplyStatus(status.Status, status.At, status.Error);
        await db.SaveChangesAsync(ct);
    }

    private async Task<PipelineStage> EntryStageAsync(CancellationToken ct) =>
        await (from p in db.Pipelines
               join s in db.PipelineStages on p.Id equals s.PipelineId
               where p.IsDefault && !s.IsArchived && s.Kind == StageKind.Open
               orderby s.Position
               select s).FirstOrDefaultAsync(ct)
        ?? throw new DomainException("pipeline.missing_default", "Nenhum pipeline padrão configurado.");

    /// <summary>Responsável padrão do canal; se estiver inativo, o primeiro administrador ativo.</summary>
    private async Task<Guid> ResolveOwnerAsync(Channel channel, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var active = db.Memberships.Where(m => m.Status == MembershipStatus.Active && (m.ExpiresAt == null || m.ExpiresAt > now));
        if (await active.AnyAsync(m => m.UserId == channel.DefaultOwnerUserId, ct))
            return channel.DefaultOwnerUserId;

        return await (from m in active
                      join r in db.Roles on m.RoleId equals r.Id
                      join u in db.Users on m.UserId equals u.Id
                      where r.Name == SystemRoles.Admin && !u.IsGuest
                      orderby m.CreatedAt
                      select m.UserId).FirstOrDefaultAsync(ct);
    }

    private void Audit(Guid organizationId, DateTimeOffset at, string action, string entityType, Guid entityId, object data) =>
        db.AuditEvents.Add(new AuditEvent(organizationId, at, ActorKind.System, null, action, entityType, entityId,
            JsonSerializer.Serialize(data, JsonOptions), System.Diagnostics.Activity.Current?.TraceId.ToString()));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// Processa os webhooks gravados (resposta rápida à Meta; trabalho pesado aqui, com retry).
/// O tenant é resolvido pelo phone_number_id do CANAL cadastrado — nunca por dados livres do payload.
/// </summary>
public sealed class WebhookProcessor(IServiceScopeFactory scopes, TimeProvider clock, ILogger<WebhookProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await ProcessBatchAsync(stoppingToken) == 0)
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no ciclo de webhooks");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var now = clock.GetUtcNow();
        var batch = await db.InboundWebhooks
            .FromSqlInterpolated($"""
                SELECT * FROM inbox.inbound_webhooks
                WHERE processed_at IS NULL AND attempts < {InboundWebhook.MaxAttempts} AND available_at <= {now}
                ORDER BY received_at
                LIMIT 20
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var webhook in batch)
        {
            try
            {
                await ProcessAsync(webhook, ct);
                webhook.MarkProcessed(clock.GetUtcNow());
            }
            catch (Exception ex)
            {
                webhook.MarkFailed(ex.Message, clock.GetUtcNow());
                logger.LogWarning(ex, "Webhook {WebhookId} falhou (tentativa {Attempt})", webhook.Id, webhook.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }

    private async Task ProcessAsync(InboundWebhook webhook, CancellationToken ct)
    {
        var (messages, statuses) = WhatsAppWebhook.Parse(webhook.Payload);

        foreach (var group in messages.Cast<object>().Concat(statuses).GroupBy(e => e is InboundMessageEvent m ? m.PhoneNumberId : ((StatusEvent)e).PhoneNumberId))
        {
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();

            var channel = await db.Channels.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(c => c.Provider == ChannelProvider.WhatsAppCloud && c.ExternalAccountId == group.Key && c.Status == ChannelStatus.Active, ct);
            if (channel is null)
            {
                logger.LogWarning("Webhook para phone_number_id não cadastrado/ativo foi ignorado.");
                continue;
            }

            sp.GetRequiredService<RequestContext>().InitializeSystem(channel.OrganizationId);
            var ingestion = sp.GetRequiredService<InboundIngestion>();
            foreach (var item in group)
            {
                if (item is InboundMessageEvent m)
                    await ingestion.IngestAsync(channel, m.FromE164, m.ProfileName, m.ExternalId, m.Type, m.Body, m.At, ct);
                else
                    await ingestion.ApplyStatusAsync((StatusEvent)item, ct);
            }
        }
    }
}
