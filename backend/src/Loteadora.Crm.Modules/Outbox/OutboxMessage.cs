namespace Loteadora.Crm.Modules.Outbox;

/// <summary>
/// Transactional outbox: eventos de domínio gravados na mesma transação do estado
/// e despachados de forma assíncrona (automações, notificações, Copilot) com retry.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public OutboxMessage(Guid organizationId, string type, string payload, DateTimeOffset occurredAt, string? correlationId)
    {
        OrganizationId = organizationId;
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
        AvailableAt = occurredAt;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public string? CorrelationId { get; private set; }

    public const int MaxAttempts = 10;

    public bool IsDeadLettered => ProcessedAt is null && Attempts >= MaxAttempts;

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt = now;

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        // Backoff exponencial com teto de 1h.
        var delay = TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, Attempts) * 5));
        AvailableAt = now + delay;
    }
}
