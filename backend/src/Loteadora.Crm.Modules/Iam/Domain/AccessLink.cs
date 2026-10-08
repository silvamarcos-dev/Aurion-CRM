using Loteadora.Crm.SharedKernel.Domain;

namespace Loteadora.Crm.Modules.Iam.Domain;

/// <summary>
/// Link de acesso externo (visitante/desenvolvedor). O token só existe em claro no momento da criação;
/// o banco guarda apenas o hash. Expira e pode ser revogado — revogar encerra o acesso imediatamente.
/// </summary>
public sealed class AccessLink : Entity, ITenantOwned, IHasTimestamps
{
    private AccessLink() { }

    public AccessLink(Guid organizationId, Guid guestUserId, string roleName, string label, string? note, string tokenHash, DateTimeOffset expiresAt, Guid createdById)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new DomainException("access_link.label_required", "Informe para quem é o acesso.");

        OrganizationId = organizationId;
        GuestUserId = guestUserId;
        RoleName = roleName;
        Label = Guard.MaxLength(label.Trim(), 120, "Nome")!;
        Note = Guard.MaxLength(note?.Trim(), 300, "Observação");
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedById = createdById;
    }

    public Guid OrganizationId { get; private set; }
    public Guid GuestUserId { get; private set; }
    public string RoleName { get; private set; } = default!;
    public string Label { get; private set; } = default!;
    public string? Note { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid CreatedById { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public int UseCount { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void RegisterUse(DateTimeOffset now)
    {
        LastUsedAt = now;
        UseCount++;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
