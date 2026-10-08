using System.Security.Cryptography;
using System.Text;
using Loteadora.Crm.SharedKernel.Security;

namespace Loteadora.Crm.Modules.Audit.Domain;

/// <summary>
/// Evento de auditoria append-only, encadeado por hash por organização (detecção de adulteração).
/// UPDATE/DELETE são bloqueados por trigger no banco.
/// "Data" NÃO deve conter dados pessoais em claro — apenas identificadores, nomes de campos e valores não sensíveis.
/// </summary>
public sealed class AuditEvent
{
    private AuditEvent() { }

    public AuditEvent(
        Guid organizationId,
        DateTimeOffset occurredAt,
        ActorKind actorKind,
        Guid? actorId,
        string action,
        string entityType,
        Guid? entityId,
        string dataJson,
        string? correlationId)
    {
        OrganizationId = organizationId;
        // PostgreSQL armazena microssegundos; truncar garante que o hash recalculado após leitura seja idêntico.
        OccurredAt = new DateTimeOffset(occurredAt.UtcTicks - (occurredAt.UtcTicks % 10), TimeSpan.Zero);
        ActorKind = actorKind;
        ActorId = actorId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Data = dataJson;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; private set; }

    /// <summary>Sequência monotônica por organização, atribuída no momento da gravação.</summary>
    public long Sequence { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
    public ActorKind ActorKind { get; private set; }
    public Guid? ActorId { get; private set; }
    public string Action { get; private set; } = default!;
    public string EntityType { get; private set; } = default!;
    public Guid? EntityId { get; private set; }
    public string Data { get; private set; } = "{}";
    public string? CorrelationId { get; private set; }
    public string PreviousHash { get; private set; } = default!;
    public string Hash { get; private set; } = default!;

    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    internal void Seal(long sequence, string previousHash)
    {
        Sequence = sequence;
        PreviousHash = previousHash;
        Hash = ComputeHash();
    }

    public string ComputeHash()
    {
        var canonical = string.Join('|',
            PreviousHash,
            OrganizationId.ToString("N"),
            Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OccurredAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (int)ActorKind,
            ActorId?.ToString("N") ?? "-",
            Action,
            EntityType,
            EntityId?.ToString("N") ?? "-",
            Data);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
