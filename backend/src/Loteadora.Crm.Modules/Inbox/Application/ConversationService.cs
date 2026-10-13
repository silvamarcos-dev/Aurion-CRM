using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.Crm.Application;
using Loteadora.Crm.Modules.Crm.Domain;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Inbox.Domain;
using Loteadora.Crm.Modules.Inbox.Providers;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Loteadora.Crm.Modules.Inbox.Application;

public enum ConversationFilter
{
    Mine = 1,
    Unassigned = 2,
    All = 3,
    Unread = 4,
}

public sealed record ConversationSummaryDto(Guid Id, Guid LeadId, string ContactName, string ContactAddress, Guid ChannelId, string ChannelName, ChannelProvider Provider,
    ConversationState State, ConversationOwnerKind OwnerKind, Guid? OwnerUserId, string? OwnerName, DateTimeOffset? LastMessageAt, string? LastMessagePreview,
    MessageDirection? LastMessageDirection, int UnreadCount, DateTimeOffset? WindowEndsAt, Guid StageId);

public sealed record MessageDto(Guid Id, MessageDirection Direction, string AuthorKind, Guid? AuthorUserId, string? AuthorName, MessageType Type, string Body,
    string? TemplateName, MessageStatus Status, string? FailureReason, DateTimeOffset OccurredAt, DateTimeOffset? DeliveredAt, DateTimeOffset? ReadAt);

public sealed record TemplateDto(Guid Id, string Name, string Language, string Body, int ParameterCount);

public sealed record ConversationDetailDto(ConversationSummaryDto Conversation, bool WithinWindow, bool CanReply, bool ContentHidden,
    IReadOnlyList<MessageDto> Messages, IReadOnlyList<TemplateDto> Templates);

public sealed record SendMessageCommand(string ClientMessageId, string? Text, Guid? TemplateId, IReadOnlyList<string>? TemplateParameters);

public sealed record AssignConversationCommand(Guid UserId);

public sealed record InboxCountersDto(int MineUnread, int Unassigned);

/// <summary>
/// Caixa de entrada. A visibilidade de uma conversa segue a do LEAD (escopo de conversations.read sobre o dono do lead).
/// Envio: idempotente por ClientMessageId, respeita a janela de 24h do WhatsApp (fora dela só template),
/// grava a mensagem ANTES de chamar o provedor e registra sucesso/falha.
/// Dados pessoais: sem pii.view, conteúdo das mensagens e contato ficam ocultos.
/// </summary>
public sealed class ConversationService(
    AppDbContext db,
    ICurrentActor actor,
    IAuditTrail audit,
    MemberScope members,
    PiiMask mask,
    MessagingProviderRegistry providers,
    IDataProtectionProvider dataProtection,
    TimeProvider clock,
    ILogger<ConversationService> logger)
{
    public const string TokenProtectorPurpose = "Inbox.ChannelAccessToken";
    private const string HiddenContent = "[conteúdo oculto no modo visitante]";

    public async Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(ConversationFilter filter, ConversationState? state, string? search, int limit, CancellationToken ct)
    {
        var query = Visible(Capabilities.ConversationsRead);
        var me = actor.UserId;
        query = filter switch
        {
            ConversationFilter.Mine => query.Where(c => c.OwnerUserId == me),
            ConversationFilter.Unassigned => query.Where(c => c.OwnerKind == ConversationOwnerKind.Unassigned),
            ConversationFilter.Unread => query.Where(c => c.UnreadCount > 0),
            _ => query,
        };
        if (state is { } s)
            query = query.Where(c => c.State == s);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            var contactIds = db.Contacts.Where(x => EF.Functions.ILike(x.FullName, term)).Select(x => x.Id);
            query = mask.IsMasking
                ? query.Where(c => contactIds.Contains(c.ContactId))
                : query.Where(c => contactIds.Contains(c.ContactId) || EF.Functions.ILike(c.ContactAddress, term));
        }

        var rows = await Project(query.OrderByDescending(c => c.LastMessageAt).Take(Math.Clamp(limit, 1, 200))).ToListAsync(ct);
        return rows.Select(Mask).ToList();
    }

    public async Task<InboxCountersDto> GetCountersAsync(CancellationToken ct)
    {
        if (!actor.Has(Capabilities.ConversationsRead))
            return new InboxCountersDto(0, 0);
        var me = actor.UserId;
        var visible = Visible(Capabilities.ConversationsRead).Where(c => c.State == ConversationState.Open);
        return new InboxCountersDto(
            await visible.CountAsync(c => c.OwnerUserId == me && c.UnreadCount > 0, ct),
            await visible.CountAsync(c => c.OwnerKind == ConversationOwnerKind.Unassigned, ct));
    }

    public async Task<ConversationDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var summary = await Project(Visible(Capabilities.ConversationsRead).Where(c => c.Id == id)).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Conversa");

        var messages = await (
                from m in db.Messages.AsNoTracking()
                where m.ConversationId == id
                join u in db.Users on m.AuthorUserId equals (Guid?)u.Id into us
                from u in us.DefaultIfEmpty()
                orderby m.OccurredAt descending
                select new MessageDto(m.Id, m.Direction, m.IsFromLead ? "Lead" : m.AuthorKind.ToString(), m.AuthorUserId, u != null ? u.DisplayName : null,
                    m.Type, m.Body, m.TemplateName, m.Status, m.FailureReason, m.OccurredAt, m.DeliveredAt, m.ReadAt))
            .Take(200)
            .ToListAsync(ct);
        messages.Reverse();

        var templates = await db.MessageTemplates.AsNoTracking().Where(t => t.ChannelId == summary.ChannelId).OrderBy(t => t.Name)
            .Select(t => new TemplateDto(t.Id, t.Name, t.Language, t.Body, t.ParameterCount)).ToListAsync(ct);

        var canReply = await CanAccessAsync(id, Capabilities.ConversationsReply, ct);
        var within = summary.WindowEndsAt is { } end && end > clock.GetUtcNow();
        if (mask.IsMasking)
            messages = messages.Select(m => m with { Body = HiddenContent, FailureReason = null }).ToList();

        return new ConversationDetailDto(Mask(summary), within, canReply, mask.IsMasking, messages, templates);
    }

    public async Task<IReadOnlyList<ConversationSummaryDto>> ListForLeadAsync(Guid leadId, CancellationToken ct)
    {
        var rows = await Project(Visible(Capabilities.ConversationsRead).Where(c => c.LeadId == leadId).OrderByDescending(c => c.LastMessageAt)).ToListAsync(ct);
        return rows.Select(Mask).ToList();
    }

    /// <summary>
    /// Abertura da conversa pelo usuário (chamada uma vez por abertura, não a cada atualização da tela):
    /// registra o acesso (auditoria LGPD) e zera os não lidos se quem abriu é o responsável.
    /// </summary>
    public async Task OpenAsync(Guid id, CancellationToken ct)
    {
        var conversation = await LoadAsync(id, Capabilities.ConversationsRead, ct);
        var canReply = await CanAccessAsync(id, Capabilities.ConversationsReply, ct);
        if (canReply && (conversation.OwnerUserId == actor.UserId || conversation.OwnerKind == ConversationOwnerKind.Unassigned))
            conversation.MarkRead();
        audit.Record("conversation.viewed", "conversation", id, new { masked = mask.IsMasking });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Marca como lida quando chegam mensagens com a conversa aberta na tela (sem nova auditoria de acesso).</summary>
    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var conversation = await LoadAsync(id, Capabilities.ConversationsReply, ct);
        if (conversation.UnreadCount > 0 && (conversation.OwnerUserId == actor.UserId || conversation.OwnerKind == ConversationOwnerKind.Unassigned))
        {
            conversation.MarkRead();
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<MessageDto> SendAsync(Guid id, SendMessageCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.ClientMessageId) || cmd.ClientMessageId.Length > 64)
            throw new DomainException("message.client_id_required", "Identificador do envio inválido.");

        var conversation = await LoadAsync(id, Capabilities.ConversationsReply, ct);

        // Idempotência: reenvio do mesmo clique/rede instável não duplica a mensagem.
        var duplicate = await db.Messages.FirstOrDefaultAsync(m => m.ConversationId == id && m.ClientMessageId == cmd.ClientMessageId, ct);
        if (duplicate is not null)
            return ToDto(duplicate, null);

        var channel = await db.Channels.FirstAsync(c => c.Id == conversation.ChannelId, ct);
        if (channel.Status != ChannelStatus.Active)
            throw new DomainException("channel.disabled", "Este canal está desativado.");

        var now = clock.GetUtcNow();
        MessageTemplate? template = null;
        IReadOnlyList<string> parameters = [];
        string body;
        if (cmd.TemplateId is { } templateId)
        {
            template = await db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.ChannelId == channel.Id, ct)
                       ?? throw new NotFoundException("Template");
            parameters = cmd.TemplateParameters ?? [];
            body = template.Render(parameters);
        }
        else
        {
            if (!conversation.IsWithinCustomerServiceWindow(now))
                throw new DomainException("message.outside_window",
                    "Fora da janela de 24h do WhatsApp: o cliente não falou com você nas últimas 24 horas. Use um template aprovado para retomar o contato.");
            body = cmd.Text ?? "";
        }

        var message = Message.Outbound(actor.OrganizationId, conversation.Id, actor.Kind, actor.Kind == ActorKind.System ? null : actor.UserId,
            template is null ? MessageType.Text : MessageType.Template, body, template?.Name, cmd.ClientMessageId, now);
        db.Messages.Add(message);
        conversation.RegisterOutbound(message);
        if (conversation.OwnerKind == ConversationOwnerKind.Unassigned && actor.Kind == ActorKind.User)
            conversation.AssignTo(actor.UserId);

        var lead = await db.Leads.FirstAsync(l => l.Id == conversation.LeadId, ct);
        lead.RegisterContact(now);
        audit.Record("message.sent", "lead", lead.Id, new { conversationId = id, messageId = message.Id, type = message.Type.ToString(), template = template?.Name });
        await db.SaveChangesAsync(ct);

        // Envio ao provedor FORA da transação: a mensagem já está registrada (Queued) e o resultado é gravado em seguida.
        await DeliverAsync(channel, conversation, message, template, parameters, ct);
        return ToDto(message, null);
    }

    public async Task<MessageDto> RetryAsync(Guid conversationId, Guid messageId, CancellationToken ct)
    {
        var conversation = await LoadAsync(conversationId, Capabilities.ConversationsReply, ct);
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId, ct) ?? throw new NotFoundException("Mensagem");
        if (message.Status != MessageStatus.Failed)
            throw new DomainException("message.not_failed", "Só é possível reenviar mensagens que falharam.");
        if (message.Type == MessageType.Text && !conversation.IsWithinCustomerServiceWindow(clock.GetUtcNow()))
            throw new DomainException("message.outside_window", "Fora da janela de 24h: use um template aprovado.");

        var channel = await db.Channels.FirstAsync(c => c.Id == conversation.ChannelId, ct);
        var template = message.TemplateName is null ? null : await db.MessageTemplates.FirstOrDefaultAsync(t => t.ChannelId == channel.Id && t.Name == message.TemplateName, ct);
        audit.Record("message.retried", "conversation", conversationId, new { messageId });
        await DeliverAsync(channel, conversation, message, template, [], ct);
        return ToDto(message, null);
    }

    public async Task AssignAsync(Guid id, AssignConversationCommand cmd, CancellationToken ct)
    {
        var conversation = await LoadAsync(id, Capabilities.ConversationsAssign, ct);
        var target = await members.RequireUserAsync(Capabilities.ConversationsAssign, cmd.UserId, ct);
        var previous = conversation.OwnerUserId;
        conversation.AssignTo(target);
        audit.Record("conversation.assigned", "conversation", id, new { previousOwnerUserId = previous, ownerUserId = target });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetStateAsync(Guid id, ConversationState state, CancellationToken ct)
    {
        var conversation = await LoadAsync(id, Capabilities.ConversationsReply, ct);
        if (state == ConversationState.Closed) conversation.Close(); else conversation.Reopen();
        audit.Record(state == ConversationState.Closed ? "conversation.closed" : "conversation.reopened", "conversation", id);
        await db.SaveChangesAsync(ct);
    }

    // ───────────────────────── helpers ─────────────────────────

    private async Task DeliverAsync(Channel channel, Conversation conversation, Message message, MessageTemplate? template, IReadOnlyList<string> parameters, CancellationToken ct)
    {
        string? token = null;
        if (channel.EncryptedAccessToken is { } encrypted)
            token = dataProtection.CreateProtector(TokenProtectorPurpose).Unprotect(encrypted);

        SendResult result;
        try
        {
            result = await providers.For(channel.Provider).SendAsync(new OutgoingMessage(channel, token, conversation.ContactAddress, message.Type, message.Body,
                template?.Name, template?.Language, parameters), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Falha de comunicação com o provedor ao enviar {MessageId}", message.Id);
            result = SendResult.Fail("Não foi possível falar com o WhatsApp. Tente reenviar.");
        }

        if (result.Success)
        {
            message.MarkSent(result.ExternalId!);
            if (channel.Provider == ChannelProvider.Simulator)
                message.ApplyStatus(MessageStatus.Delivered, clock.GetUtcNow(), null);
        }
        else
        {
            message.MarkFailed(result.Error ?? "Falha no envio.");
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Conversas cujo LEAD está no escopo da capability (o filtro de tenant já é global).</summary>
    private IQueryable<Conversation> Visible(string capability)
    {
        var scope = actor.Require(capability);
        var leadIds = LeadScope.Apply(db, actor, scope).Select(l => l.Id);
        return db.Conversations.Where(c => leadIds.Contains(c.LeadId));
    }

    private async Task<bool> CanAccessAsync(Guid id, string capability, CancellationToken ct) =>
        actor.Has(capability) && await Visible(capability).AnyAsync(c => c.Id == id, ct);

    private async Task<Conversation> LoadAsync(Guid id, string capability, CancellationToken ct) =>
        await Visible(capability).FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Conversa");

    private IQueryable<ConversationSummaryDto> Project(IQueryable<Conversation> query) =>
        from c in query.AsNoTracking()
        join ct in db.Contacts on c.ContactId equals ct.Id
        join ch in db.Channels on c.ChannelId equals ch.Id
        join l in db.Leads on c.LeadId equals l.Id
        join u in db.Users on c.OwnerUserId equals (Guid?)u.Id into us
        from u in us.DefaultIfEmpty()
        select new ConversationSummaryDto(c.Id, c.LeadId, ct.FullName, c.ContactAddress, c.ChannelId, ch.Name, ch.Provider, c.State, c.OwnerKind, c.OwnerUserId,
            u != null ? u.DisplayName : null, c.LastMessageAt, c.LastMessagePreview, c.LastMessageDirection, c.UnreadCount,
            c.LastInboundAt == null ? null : c.LastInboundAt!.Value.AddHours(24), l.StageId);

    private ConversationSummaryDto Mask(ConversationSummaryDto c) => mask.IsMasking
        ? c with { ContactName = mask.Name(c.ContactName), ContactAddress = mask.Phone(c.ContactAddress) ?? "", LastMessagePreview = c.LastMessagePreview is null ? null : "•••" }
        : c;

    private static MessageDto ToDto(Message m, string? authorName) =>
        new(m.Id, m.Direction, m.IsFromLead ? "Lead" : m.AuthorKind.ToString(), m.AuthorUserId, authorName, m.Type, m.Body, m.TemplateName, m.Status,
            m.FailureReason, m.OccurredAt, m.DeliveredAt, m.ReadAt);
}
