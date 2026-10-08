using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Security;
using Loteadora.Crm.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Loteadora.Crm.Modules.Iam.Application;

/// <summary>
/// Contexto da requisição (escopo DI): implementa ICurrentActor e ITenantContext.
/// É preenchido UMA vez por requisição a partir do token validado, com capabilities
/// recarregadas do banco (revogação de permissão tem efeito imediato).
/// </summary>
public sealed class RequestContext : ICurrentActor, ITenantContext
{
    private IReadOnlyDictionary<string, CapabilityScope> _capabilities = new Dictionary<string, CapabilityScope>();
    private bool _initialized;

    public bool IsAuthenticated { get; private set; }
    public ActorKind Kind { get; private set; }
    public Guid UserId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid? TeamId { get; private set; }
    public bool HasTenant => IsAuthenticated;

    public CapabilityScope? ScopeOf(string capability) =>
        _capabilities.TryGetValue(capability, out var scope) ? scope : null;

    public IReadOnlyDictionary<string, CapabilityScope> Capabilities => _capabilities;

    public void InitializeUser(Guid userId, Guid organizationId, Guid? teamId, IReadOnlyDictionary<string, CapabilityScope> capabilities)
    {
        EnsureNotInitialized();
        IsAuthenticated = true;
        Kind = ActorKind.User;
        UserId = userId;
        OrganizationId = organizationId;
        TeamId = teamId;
        _capabilities = capabilities;
    }

    /// <summary>Ator de sistema (jobs, seed, automações). Usa somente as capabilities informadas.</summary>
    public void InitializeSystem(Guid organizationId, IReadOnlyDictionary<string, CapabilityScope>? capabilities = null)
    {
        EnsureNotInitialized();
        IsAuthenticated = true;
        Kind = ActorKind.System;
        OrganizationId = organizationId;
        _capabilities = capabilities ?? new Dictionary<string, CapabilityScope>();
    }

    private void EnsureNotInitialized()
    {
        if (_initialized)
            throw new InvalidOperationException("Contexto da requisição já inicializado.");
        _initialized = true;
    }
}

public sealed class ActorLoader(AppDbContext db, TimeProvider clock)
{
    public sealed record LoadedActor(Guid UserId, Guid OrganizationId, Guid? TeamId, IReadOnlyDictionary<string, CapabilityScope> Capabilities);

    /// <summary>Carrega vínculo ativo + capabilities. Retorna null se usuário/vínculo/organização estiverem inativos ou expirados.</summary>
    public async Task<LoadedActor?> LoadAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var row = await (
                from m in db.Memberships.IgnoreQueryFilters()
                join u in db.Users on m.UserId equals u.Id
                join o in db.Organizations on m.OrganizationId equals o.Id
                join r in db.Roles.IgnoreQueryFilters() on m.RoleId equals r.Id
                where m.UserId == userId && m.OrganizationId == organizationId
                      && m.Status == MembershipStatus.Active && (m.ExpiresAt == null || m.ExpiresAt > now)
                      && u.IsActive && o.IsActive
                      && r.OrganizationId == organizationId
                select new { m.TeamId, r.Capabilities })
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return null;

        var caps = row.Capabilities
            .GroupBy(c => c.Capability)
            .ToDictionary(g => g.Key, g => g.Max(c => c.Scope));

        return new LoadedActor(userId, organizationId, row.TeamId, caps);
    }
}
