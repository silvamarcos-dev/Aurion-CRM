using System.Security.Cryptography;
using System.Text;
using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Inbox.Domain;
using Loteadora.Crm.Modules.Inbox.Providers;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Loteadora.Crm.Modules.Inbox.Application;

public sealed record ChannelDto(Guid Id, ChannelProvider Provider, string Name, string PhoneNumber, string? PhoneNumberId, Guid DefaultOwnerUserId, string? DefaultOwnerName,
    ChannelStatus Status, bool HasCredentials, int TemplateCount, DateTimeOffset CreatedAt);

/// <summary>AccessToken só é usado na escrita (nunca devolvido). Vazio/nulo mantém o atual.</summary>
public sealed record SaveChannelCommand(ChannelProvider Provider, string Name, string PhoneNumber, string? PhoneNumberId, Guid DefaultOwnerUserId, string? AccessToken);

public sealed record SaveTemplateCommand(string Name, string? Language, string Body);

public sealed record SimulateInboundCommand(Guid ChannelId, string FromPhone, string? ProfileName, string Text);

public sealed record WebhookSetupDto(bool AppSecretConfigured, bool VerifyTokenConfigured, string CallbackPath);

/// <summary>Canais e templates (channels.manage). Credenciais cifradas com Data Protection e nunca retornadas.</summary>
public sealed class ChannelService(AppDbContext db, ICurrentActor actor, IAuditTrail audit, MemberScope members, IDataProtectionProvider dataProtection, IOptions<WhatsAppOptions> whatsApp)
{
    public async Task<IReadOnlyList<ChannelDto>> ListAsync(CancellationToken ct)
    {
        actor.Require(Capabilities.ConversationsRead);
        return await (
                from c in db.Channels.AsNoTracking()
                join u in db.Users on c.DefaultOwnerUserId equals u.Id into us
                from u in us.DefaultIfEmpty()
                orderby c.CreatedAt
                select new ChannelDto(c.Id, c.Provider, c.Name, c.PhoneNumberE164, c.ExternalAccountId, c.DefaultOwnerUserId, u != null ? u.DisplayName : null,
                    c.Status, c.EncryptedAccessToken != null, db.MessageTemplates.Count(t => t.ChannelId == c.Id), c.CreatedAt))
            .ToListAsync(ct);
    }

    public WebhookSetupDto GetWebhookSetup()
    {
        actor.Require(Capabilities.ChannelsManage);
        return new WebhookSetupDto(!string.IsNullOrEmpty(whatsApp.Value.AppSecret), !string.IsNullOrEmpty(whatsApp.Value.VerifyToken), "/api/webhooks/whatsapp");
    }

    public async Task<Guid> CreateAsync(SaveChannelCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.ChannelsManage);
        await EnsureOwnerAsync(cmd.DefaultOwnerUserId, ct);
        await EnsureUniqueAsync(cmd.Provider, cmd.PhoneNumberId, null, ct);

        var channel = new Channel(actor.OrganizationId, cmd.Provider, cmd.Name, cmd.PhoneNumber, cmd.Provider == ChannelProvider.WhatsAppCloud ? cmd.PhoneNumberId : null, cmd.DefaultOwnerUserId);
        SetToken(channel, cmd.AccessToken);
        db.Channels.Add(channel);
        audit.Record("channel.created", "channel", channel.Id, new { provider = cmd.Provider.ToString(), cmd.Name, credentials = channel.HasCredentials });
        await db.SaveChangesAsync(ct);
        return channel.Id;
    }

    public async Task UpdateAsync(Guid id, SaveChannelCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.ChannelsManage);
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Canal");
        await EnsureOwnerAsync(cmd.DefaultOwnerUserId, ct);
        await EnsureUniqueAsync(channel.Provider, cmd.PhoneNumberId, id, ct);

        channel.Update(cmd.Name, cmd.PhoneNumber, channel.Provider == ChannelProvider.WhatsAppCloud ? cmd.PhoneNumberId : null, cmd.DefaultOwnerUserId);
        var tokenChanged = !string.IsNullOrWhiteSpace(cmd.AccessToken);
        SetToken(channel, cmd.AccessToken);
        audit.Record("channel.updated", "channel", id, new { cmd.Name, tokenChanged });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        actor.Require(Capabilities.ChannelsManage);
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Canal");
        if (enabled) channel.Enable(); else channel.Disable();
        audit.Record(enabled ? "channel.enabled" : "channel.disabled", "channel", id);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TemplateDto>> ListTemplatesAsync(Guid channelId, CancellationToken ct)
    {
        actor.Require(Capabilities.ConversationsRead);
        return await db.MessageTemplates.AsNoTracking().Where(t => t.ChannelId == channelId).OrderBy(t => t.Name)
            .Select(t => new TemplateDto(t.Id, t.Name, t.Language, t.Body, t.ParameterCount)).ToListAsync(ct);
    }

    public async Task<Guid> CreateTemplateAsync(Guid channelId, SaveTemplateCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.ChannelsManage);
        if (!await db.Channels.AnyAsync(c => c.Id == channelId, ct))
            throw new NotFoundException("Canal");
        if (await db.MessageTemplates.AnyAsync(t => t.ChannelId == channelId && t.Name == cmd.Name && t.Language == (cmd.Language ?? "pt_BR"), ct))
            throw new DomainException("template.duplicate", "Já existe um template com esse nome e idioma.");

        var template = new MessageTemplate(actor.OrganizationId, channelId, cmd.Name, cmd.Language ?? "pt_BR", cmd.Body);
        db.MessageTemplates.Add(template);
        audit.Record("template.created", "channel", channelId, new { templateId = template.Id, template.Name });
        await db.SaveChangesAsync(ct);
        return template.Id;
    }

    public async Task DeleteTemplateAsync(Guid channelId, Guid templateId, CancellationToken ct)
    {
        actor.Require(Capabilities.ChannelsManage);
        var template = await db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.ChannelId == channelId, ct) ?? throw new NotFoundException("Template");
        db.MessageTemplates.Remove(template);
        audit.Record("template.deleted", "channel", channelId, new { templateId, template.Name });
        await db.SaveChangesAsync(ct);
    }

    private void SetToken(Channel channel, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;
        channel.SetEncryptedAccessToken(dataProtection.CreateProtector(ConversationService.TokenProtectorPurpose).Protect(token.Trim()));
    }

    private Task EnsureOwnerAsync(Guid userId, CancellationToken ct) => members.RequireUserAsync(Capabilities.ChannelsManage, userId, ct);

    private async Task EnsureUniqueAsync(ChannelProvider provider, string? phoneNumberId, Guid? exceptId, CancellationToken ct)
    {
        if (provider != ChannelProvider.WhatsAppCloud || string.IsNullOrWhiteSpace(phoneNumberId))
            return;
        // O Phone Number ID identifica o tenant no webhook: precisa ser único em TODO o sistema.
        if (await db.Channels.IgnoreQueryFilters().AnyAsync(c => c.Provider == provider && c.ExternalAccountId == phoneNumberId.Trim() && c.Id != exceptId, ct))
            throw new DomainException("channel.phone_number_id_taken", "Este Phone Number ID já está cadastrado.");
    }
}

/// <summary>"Simular cliente": gera uma mensagem RECEBIDA num canal de simulação, pelo mesmo caminho do webhook real.</summary>
public sealed class SimulatorService(AppDbContext db, ICurrentActor actor, IAuditTrail audit, InboundIngestion ingestion, TimeProvider clock)
{
    public async Task<IngestResult> InboundAsync(SimulateInboundCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.ChannelsSimulate);
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChannelId, ct) ?? throw new NotFoundException("Canal");
        if (channel.Provider != ChannelProvider.Simulator)
            throw new DomainException("simulator.real_channel", "O simulador só funciona em canais de simulação — nunca num número real.");
        if (channel.Status != ChannelStatus.Active)
            throw new DomainException("channel.disabled", "Este canal está desativado.");
        if (string.IsNullOrWhiteSpace(cmd.Text))
            throw new DomainException("message.empty", "Escreva a mensagem do cliente.");

        var result = await ingestion.IngestAsync(channel, cmd.FromPhone, cmd.ProfileName, $"sim.in.{Guid.CreateVersion7():N}", MessageType.Text, cmd.Text.Trim(), clock.GetUtcNow(), ct);
        audit.Record("simulator.inbound", "conversation", result.ConversationId, new { channelId = channel.Id, leadCreated = result.LeadCreated });
        await db.SaveChangesAsync(ct);
        return result;
    }
}

/// <summary>Recebe o POST da Meta: valida a assinatura, grava o bruto (deduplicado) e responde rápido.</summary>
public sealed class WebhookReceiver(AppDbContext db, IOptions<WhatsAppOptions> options, TimeProvider clock)
{
    public enum Outcome { Accepted, Duplicate, InvalidSignature }

    public bool VerifySubscription(string? mode, string? token) =>
        mode == "subscribe" && !string.IsNullOrEmpty(options.Value.VerifyToken) && !string.IsNullOrEmpty(token)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(options.Value.VerifyToken));

    public async Task<Outcome> ReceiveAsync(string rawBody, string? signature, CancellationToken ct)
    {
        if (!WhatsAppWebhook.IsSignatureValid(rawBody, signature, options.Value.AppSecret))
            return Outcome.InvalidSignature;

        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody)));
        if (await db.InboundWebhooks.AnyAsync(w => w.Provider == ChannelProvider.WhatsAppCloud && w.EventKey == key, ct))
            return Outcome.Duplicate;

        db.InboundWebhooks.Add(new InboundWebhook(ChannelProvider.WhatsAppCloud, key, rawBody, clock.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Outcome.Duplicate; // corrida entre entregas simultâneas: o índice único garante uma só
        }
        return Outcome.Accepted;
    }
}
