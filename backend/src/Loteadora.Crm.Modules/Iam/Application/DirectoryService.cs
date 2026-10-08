using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Loteadora.Crm.Modules.Iam.Application;

public sealed record MeDto(
    Guid UserId,
    string Email,
    string DisplayName,
    Guid OrganizationId,
    string OrganizationName,
    Guid? TeamId,
    string Role,
    IReadOnlyDictionary<string, string> Capabilities,
    bool IsGuest,
    DateTimeOffset? AccessExpiresAt);

public sealed record UserSummaryDto(Guid UserId, string DisplayName, Guid? TeamId, string Role);

/// <summary>Consultas de usuários/membros dentro da organização corrente.</summary>
public sealed class DirectoryService(AppDbContext db, RequestContext context, MemberScope members)
{
    /// <summary>Usuários sobre os quais o ator tem a capability informada (ex.: agendas visíveis).</summary>
    public async Task<IReadOnlyList<UserSummaryDto>> ListInScopeAsync(string capability, CancellationToken ct)
    {
        var scope = context.ScopeOf(capability);
        if (scope is null || !Capabilities.All.Contains(capability))
            return [];

        var ids = members.UserIds(scope.Value);
        return await (
                from m in db.Memberships
                join u in db.Users on m.UserId equals u.Id
                join r in db.Roles on m.RoleId equals r.Id
                where ids.Contains(m.UserId) && u.IsActive && !u.IsGuest
                orderby u.DisplayName
                select new UserSummaryDto(u.Id, u.DisplayName, m.TeamId, r.Name))
            .ToListAsync(ct);
    }

    public async Task<MeDto> GetMeAsync(CancellationToken ct)
    {
        var row = await (
                from u in db.Users
                join m in db.Memberships on u.Id equals m.UserId
                join o in db.Organizations on m.OrganizationId equals o.Id
                join r in db.Roles on m.RoleId equals r.Id
                where u.Id == context.UserId
                select new { u.Email, u.DisplayName, o.Name, Role = r.Name, u.IsGuest, m.ExpiresAt })
            .AsNoTracking()
            .FirstAsync(ct);

        // As capabilities enviadas ao frontend servem APENAS para UX (esconder botões).
        // A autorização real permanece no backend.
        return new MeDto(context.UserId, row.Email, row.DisplayName, context.OrganizationId, row.Name, context.TeamId, row.Role,
            context.Capabilities.ToDictionary(kv => kv.Key, kv => kv.Value.ToString().ToLowerInvariant()),
            row.IsGuest, row.ExpiresAt);
    }

    /// <summary>Usuários para os quais o ator pode atribuir leads (respeita o escopo de leads.assign).</summary>
    public async Task<IReadOnlyList<UserSummaryDto>> ListAssignableAsync(CancellationToken ct)
    {
        var scope = context.ScopeOf(Capabilities.LeadsAssign);
        var query =
            from m in db.Memberships
            join u in db.Users on m.UserId equals u.Id
            join r in db.Roles on m.RoleId equals r.Id
            where m.Status == MembershipStatus.Active && u.IsActive && !u.IsGuest
            select new { m.UserId, u.DisplayName, m.TeamId, Role = r.Name };

        query = scope switch
        {
            CapabilityScope.Org => query,
            CapabilityScope.Team when context.TeamId is { } teamId => query.Where(x => x.TeamId == teamId),
            _ => query.Where(x => x.UserId == context.UserId),
        };

        return await query.OrderBy(x => x.DisplayName)
            .Select(x => new UserSummaryDto(x.UserId, x.DisplayName, x.TeamId, x.Role))
            .ToListAsync(ct);
    }
}
