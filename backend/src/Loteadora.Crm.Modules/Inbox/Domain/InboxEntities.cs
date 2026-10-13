using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;

namespace Loteadora.Crm.Modules.Inbox.Domain;

public enum ChannelProvider
{
    /// <summary>WhatsApp simulado dentro do CRM (treinamento, demonstração, testes).</summary>
    Simulator = 1,

    /// <summary>WhatsApp Business Platform (Cloud API oficial da Meta).</summary>
    WhatsAppCloud = 2,
}

public enum ChannelStatus
{
    Active = 1,
    Disabled = 2,
}

/// <summary>
/// Canal de mensagens da organização (um número de WhatsApp). Credenciais ficam cifradas
/// (ASP.NET Data Protection) e nunca são devolvidas pela API.
/// </summary>
public sealed class Channel : Entity, ITenantOwned, IHasTimestamps
{
    private Channel() { }

    public Channel(Guid organizationId, ChannelProvider provider, string name, string phoneNumberE164, string? externalAccountId, Guid defaultOwnerUserId)
    {
        OrganizationId = organizationId;
        Provider = provider;
        DefaultOwnerUserId = defaultOwnerUserId;
        Update(name, phoneNumberE164, externalAccountId, defaultOwnerUserId);
        if (provider == ChannelProvider.WhatsAppCloud && string.IsNullOrWhiteSpace(externalAccountId))
            throw new DomainException("channel.phone_number_id_required", "Informe o Phone Number ID da Meta.");
    }

    public Guid OrganizationId { get; private set; }
    public ChannelProvider Provider { get; private set; }
    public string Name { get; private set; } = default!;
    public string PhoneNumberE164 { get; private set; } = default!;

    /// <summary>Phone Number ID da Meta — usado para identificar o tenant nos webhooks.</summary>
    public string? ExternalAccountId { get; private set; }

    /// <summary>Corretor que recebe leads NOVOS que chegam por este canal (até existir a distribuição automática).</summary>
    public Guid DefaultOwnerUserId { get; private set; }

    public string? EncryptedAccessToken { get; private set; }
    public ChannelStatus Status { get; private set; } = ChannelStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool HasCredentials => !string.IsNullOrEmpty(EncryptedAccessToken);

    public void Update(string name, string phoneNumberE164, string? externalAccountId, Guid defaultOwnerUserId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("channel.name_required", "Informe o nome do canal.");
        Name = Guard.MaxLength(name.Trim(), 80, "Nome")!;
        PhoneNumberE164 = Crm.Domain.PhoneNumber.NormalizeE164(phoneNumberE164);
        ExternalAccountId = Guard.MaxLength(externalAccountId?.Trim(), 64, "Phone Number ID");
        DefaultOwnerUserId = defaultOwnerUserId;
    }

    public void SetEncryptedAccessToken(string? encrypted) => EncryptedAccessToken = encrypted;

    public void Enable() => Status = ChannelStatus.Active;

    public void Disable() => Status = ChannelStatus.Disabled;
}

/// <summary>Template aprovado na Meta — única forma de iniciar/retomar conversa fora da janela de 24h.</summary>
public sealed class MessageTemplate : Entity, ITenantOwned, IHasTimestamps
{
    private MessageTemplate() { }

    public MessageTemplate(Guid organizationId, Guid channelId, string name, string language, string body)
    {
        if (string.IsNullOrWhiteSpace(name) || !name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
            throw new DomainException("template.name_invalid", "Nome do template: letras minúsculas, números e _ (igual ao cadastrado na Meta).");
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("template.body_required", "Informe o texto do template.");

        OrganizationId = organizationId;
        ChannelId = channelId;
        Name = Guard.MaxLength(name, 120, "Nome")!;
        Language = Guard.MaxLength(string.IsNullOrWhiteSpace(language) ? "pt_BR" : language.Trim(), 10, "Idioma")!;
        Body = Guard.MaxLength(body.Trim(), 1024, "Texto")!;
        ParameterCount = CountParameters(Body);
    }

    public Guid OrganizationId { get; private set; }
    public Guid ChannelId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Language { get; private set; } = default!;

    /// <summary>Texto com {{1}}, {{2}}… exatamente como aprovado na Meta (usado para pré-visualizar e registrar).</summary>
    public string Body { get; private set; } = default!;

    public int ParameterCount { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public string Render(IReadOnlyList<string> parameters)
    {
        if (parameters.Count != ParameterCount)
            throw new DomainException("template.parameters", $"O template exige {ParameterCount} parâmetro(s).");
        var text = Body;
        for (var i = 0; i < parameters.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(parameters[i]))
                throw new DomainException("template.parameter_empty", $"Preencha o parâmetro {i + 1}.");
            text = text.Replace($"{{{{{i + 1}}}}}", parameters[i].Trim(), StringComparison.Ordinal);
        }
        return text;
    }

    private static int CountParameters(string body)
    {
        var max = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(body, @"\{\{(\d+)\}\}"))
            max = Math.Max(max, int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        return max;
    }
}

public enum ConversationState
{
    Open = 1,
    Closed = 2,
}

public enum ConversationOwnerKind
{
    Unassigned = 0,
    User = 1,
    Copilot = 2,
}

/// <summary>
/// Conversa de um lead em um canal. Guarda o "dono" atual e a época de posse (ownership epoch),
/// base do controle de concorrência humano × Copilot da Fase 8.
/// </summary>
public sealed class Conversation : Entity, ITenantOwned, IHasTimestamps
{
    public static readonly TimeSpan CustomerServiceWindow = TimeSpan.FromHours(24);

    private Conversation() { }

    public Conversation(Guid organizationId, Guid channelId, Guid leadId, Guid contactId, string contactAddress, Guid? ownerUserId)
    {
        OrganizationId = organizationId;
        ChannelId = channelId;
        LeadId = leadId;
        ContactId = contactId;
        ContactAddress = contactAddress;
        if (ownerUserId is { } owner)
            AssignTo(owner);
    }

    public Guid OrganizationId { get; private set; }
    public Guid ChannelId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid ContactId { get; private set; }

    /// <summary>Número do cliente em E.164.</summary>
    public string ContactAddress { get; private set; } = default!;

    public ConversationState State { get; private set; } = ConversationState.Open;
    public ConversationOwnerKind OwnerKind { get; private set; } = ConversationOwnerKind.Unassigned;
    public Guid? OwnerUserId { get; private set; }
    public int OwnershipEpoch { get; private set; }
    public DateTimeOffset? LastInboundAt { get; private set; }
    public DateTimeOffset? LastOutboundAt { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }
    public string? LastMessagePreview { get; private set; }
    public MessageDirection? LastMessageDirection { get; private set; }

    /// <summary>Mensagens recebidas ainda não lidas pelo responsável.</summary>
    public int UnreadCount { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; private set; }

    /// <summary>Janela de atendimento do WhatsApp: 24h após a última mensagem DO CLIENTE.</summary>
    public DateTimeOffset? CustomerServiceWindowEndsAt => LastInboundAt?.Add(CustomerServiceWindow);

    public bool IsWithinCustomerServiceWindow(DateTimeOffset now) => CustomerServiceWindowEndsAt is { } end && end > now;

    public void RegisterInbound(Message message)
    {
        LastInboundAt = Max(LastInboundAt, message.OccurredAt);
        Touch(message.OccurredAt, Preview(message), MessageDirection.Inbound);
        UnreadCount++;
        if (State == ConversationState.Closed)
            State = ConversationState.Open;
        Raise(new MessageReceived(OrganizationId, Id, LeadId, message.Id, OwnerUserId, message.OccurredAt));
    }

    public void RegisterOutbound(Message message)
    {
        LastOutboundAt = Max(LastOutboundAt, message.OccurredAt);
        Touch(message.OccurredAt, Preview(message), MessageDirection.Outbound);
        UnreadCount = 0;
        Raise(new MessageSent(OrganizationId, Id, LeadId, message.Id, message.AuthorKind, message.AuthorUserId, message.OccurredAt));
    }

    private static string Preview(Message message) => message.Type switch
    {
        MessageType.Text or MessageType.Template => message.Body,
        MessageType.Image => "📷 Imagem",
        MessageType.Audio => "🎤 Áudio",
        MessageType.Video => "🎬 Vídeo",
        MessageType.Document => "📄 Documento",
        _ => "Mensagem",
    };

    public void MarkRead() => UnreadCount = 0;

    public void AssignTo(Guid userId)
    {
        if (OwnerKind == ConversationOwnerKind.User && OwnerUserId == userId)
            return;
        OwnerKind = ConversationOwnerKind.User;
        OwnerUserId = userId;
        OwnershipEpoch++;
    }

    public void Close() => State = ConversationState.Closed;

    public void Reopen() => State = ConversationState.Open;

    private void Touch(DateTimeOffset at, string preview, MessageDirection direction)
    {
        if (LastMessageAt is null || at >= LastMessageAt)
        {
            LastMessageAt = at;
            LastMessagePreview = preview.Length > 140 ? preview[..140] + "…" : preview;
            LastMessageDirection = direction;
        }
    }

    private static DateTimeOffset Max(DateTimeOffset? a, DateTimeOffset b) => a is { } x && x > b ? x : b;
}

public enum MessageDirection
{
    Inbound = 1,
    Outbound = 2,
}

public enum MessageType
{
    Text = 1,
    Template = 2,
    Image = 3,
    Document = 4,
    Audio = 5,
    Video = 6,
    Other = 7,
}

public enum MessageStatus
{
    Received = 1,
    Queued = 2,
    Sent = 3,
    Delivered = 4,
    Read = 5,
    Failed = 6,
}

public sealed class Message : Entity, ITenantOwned
{
    private Message() { }

    private Message(Guid organizationId, Guid conversationId, MessageDirection direction, ActorKind authorKind, Guid? authorUserId,
        MessageType type, string body, DateTimeOffset occurredAt, MessageStatus status)
    {
        OrganizationId = organizationId;
        ConversationId = conversationId;
        Direction = direction;
        AuthorKind = authorKind;
        AuthorUserId = authorUserId;
        Type = type;
        Body = Guard.MaxLength(body, 4096, "Mensagem")!;
        OccurredAt = occurredAt;
        Status = status;
    }

    public static Message Inbound(Guid organizationId, Guid conversationId, string externalId, MessageType type, string body, DateTimeOffset at) =>
        new(organizationId, conversationId, MessageDirection.Inbound, ActorKind.User, null, type, body, at, MessageStatus.Received) { ExternalId = externalId, IsFromLead = true };

    public static Message Outbound(Guid organizationId, Guid conversationId, ActorKind authorKind, Guid? authorUserId, MessageType type, string body,
        string? templateName, string clientMessageId, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("message.empty", "A mensagem está vazia.");
        return new Message(organizationId, conversationId, MessageDirection.Outbound, authorKind, authorUserId, type, body.Trim(), at, MessageStatus.Queued)
        {
            TemplateName = templateName,
            ClientMessageId = clientMessageId,
        };
    }

    public Guid OrganizationId { get; private set; }
    public Guid ConversationId { get; private set; }
    public MessageDirection Direction { get; private set; }

    /// <summary>Para mensagens recebidas o autor é o lead (IsFromLead); para enviadas, usuário/Copilot/sistema.</summary>
    public ActorKind AuthorKind { get; private set; }

    public bool IsFromLead { get; private set; }
    public Guid? AuthorUserId { get; private set; }
    public MessageType Type { get; private set; }
    public string Body { get; private set; } = default!;
    public string? TemplateName { get; private set; }

    /// <summary>Identificador no provedor (wamid). Único por organização — garante idempotência de webhooks.</summary>
    public string? ExternalId { get; private set; }

    /// <summary>Chave de idempotência do envio (gerada pelo cliente).</summary>
    public string? ClientMessageId { get; private set; }

    public MessageStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkSent(string externalId)
    {
        ExternalId = externalId;
        Status = MessageStatus.Sent;
        FailureReason = null;
    }

    public void MarkFailed(string reason)
    {
        Status = MessageStatus.Failed;
        FailureReason = reason.Length > 300 ? reason[..300] : reason;
    }

    /// <summary>Status só avança (Sent → Delivered → Read); webhooks fora de ordem não regridem o estado.</summary>
    public void ApplyStatus(MessageStatus status, DateTimeOffset at, string? failureReason)
    {
        if (Direction != MessageDirection.Outbound)
            return;
        switch (status)
        {
            case MessageStatus.Failed:
                MarkFailed(failureReason ?? "Falha informada pelo provedor.");
                break;
            case MessageStatus.Delivered when Status is MessageStatus.Queued or MessageStatus.Sent:
                Status = MessageStatus.Delivered;
                DeliveredAt = at;
                break;
            case MessageStatus.Read when Status is MessageStatus.Queued or MessageStatus.Sent or MessageStatus.Delivered:
                Status = MessageStatus.Read;
                DeliveredAt ??= at;
                ReadAt = at;
                break;
        }
    }
}

public sealed record MessageReceived(Guid OrganizationId, Guid ConversationId, Guid LeadId, Guid MessageId, Guid? OwnerUserId, DateTimeOffset At) : IDomainEvent;

public sealed record MessageSent(Guid OrganizationId, Guid ConversationId, Guid LeadId, Guid MessageId, ActorKind AuthorKind, Guid? AuthorUserId, DateTimeOffset At) : IDomainEvent;

/// <summary>
/// Webhook bruto recebido do provedor (gravado ANTES do processamento, respondido rápido).
/// Deduplicado por (provedor, id do evento). Contém dados pessoais: retenção curta (ver política).
/// </summary>
public sealed class InboundWebhook
{
    private InboundWebhook() { }

    public InboundWebhook(ChannelProvider provider, string eventKey, string payload, DateTimeOffset receivedAt)
    {
        Provider = provider;
        EventKey = eventKey;
        Payload = payload;
        ReceivedAt = receivedAt;
        AvailableAt = receivedAt;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public ChannelProvider Provider { get; private set; }
    public string EventKey { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public const int MaxAttempts = 8;

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt = now;

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        AvailableAt = now + TimeSpan.FromSeconds(Math.Min(1800, Math.Pow(2, Attempts) * 5));
    }
}
