using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;

namespace Loteadora.Crm.Modules.Iam.Domain;

public sealed class Organization : Entity, IHasTimestamps
{
    private Organization() { }

    public Organization(string name, string slug, string timeZone = "America/Sao_Paulo")
    {
        Name = name;
        Slug = slug;
        TimeZone = timeZone;
    }

    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public string TimeZone { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Team : Entity, ITenantOwned, IHasTimestamps
{
    private Team() { }

    public Team(Guid organizationId, string name)
    {
        OrganizationId = organizationId;
        Name = name;
    }

    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Usuário global (credenciais). O vínculo com organizações é feito por Membership.</summary>
public sealed class User : Entity, IHasTimestamps
{
    private User() { }

    public User(string email, string displayName)
    {
        Email = NormalizeEmail(email);
        DisplayName = displayName;
    }

    /// <summary>Usuário convidado (acesso por link, sem senha). Nunca autentica por e-mail/senha.</summary>
    public static User CreateGuest(string displayName)
    {
        var user = new User($"convidado-{Guid.NewGuid():N}@convidado.invalid", displayName) { IsGuest = true };
        user.PasswordHash = "";
        return user;
    }

    public bool IsGuest { get; private set; }

    public string Email { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public string FirstName => DisplayName.Split(' ', 2)[0];

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;

    public void RegisterFailedLogin(DateTimeOffset now)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= 5)
        {
            LockedUntil = now.AddMinutes(15);
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    public void Deactivate() => IsActive = false;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

public sealed class Role : Entity, ITenantOwned, IHasTimestamps
{
    private readonly List<RoleCapability> _capabilities = [];

    private Role() { }

    public Role(Guid organizationId, string name, bool isSystem)
    {
        OrganizationId = organizationId;
        Name = name;
        IsSystem = isSystem;
    }

    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = default!;
    public bool IsSystem { get; private set; }
    public IReadOnlyList<RoleCapability> Capabilities => _capabilities;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Grant(string capability, CapabilityScope scope)
    {
        if (!SharedKernel.Security.Capabilities.All.Contains(capability))
            throw new DomainException("capability.unknown", $"Capability desconhecida: {capability}");

        _capabilities.RemoveAll(c => c.Capability == capability);
        _capabilities.Add(new RoleCapability(Id, capability, scope));
    }
}

public sealed class RoleCapability
{
    private RoleCapability() { }

    internal RoleCapability(Guid roleId, string capability, CapabilityScope scope)
    {
        RoleId = roleId;
        Capability = capability;
        Scope = scope;
    }

    public Guid RoleId { get; private set; }
    public string Capability { get; private set; } = default!;
    public CapabilityScope Scope { get; private set; }
}

public enum MembershipStatus
{
    Active = 1,
    Suspended = 2,
    Revoked = 3,
}

/// <summary>Vínculo usuário ↔ organização, com papel e time.</summary>
public sealed class Membership : Entity, ITenantOwned, IHasTimestamps
{
    private Membership() { }

    public Membership(Guid organizationId, Guid userId, Guid roleId, Guid? teamId)
    {
        OrganizationId = organizationId;
        UserId = userId;
        RoleId = roleId;
        TeamId = teamId;
    }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? TeamId { get; private set; }
    public MembershipStatus Status { get; private set; } = MembershipStatus.Active;

    /// <summary>Vínculos temporários (convidados) expiram automaticamente.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsActive(DateTimeOffset now) => Status == MembershipStatus.Active && (ExpiresAt is null || ExpiresAt > now);

    public void ExpireAt(DateTimeOffset expiresAt) => ExpiresAt = expiresAt;

    public void Revoke() => Status = MembershipStatus.Revoked;
}

/// <summary>
/// Refresh token rotativo. Somente o hash é armazenado. Reuso de um token já rotacionado
/// revoga toda a família (detecção de roubo de token).
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { }

    public RefreshToken(Guid userId, Guid organizationId, Guid familyId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset createdAt)
    {
        UserId = userId;
        OrganizationId = organizationId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, Guid? replacedBy = null)
    {
        RevokedAt ??= now;
        ReplacedById ??= replacedBy;
    }
}
