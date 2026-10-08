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
    IReadOnlyDictionary<string, string> Capabilities);

public sealed record UserSummaryDto(Guid UserId, string DisplayName, Guid? TeamId, string Role);

/// <summary>Consultas de usuários/membros dentro da organização corrente.</summary>
public sealed class DirectoryService(AppDbContext db, RequestContext context)
{
    public async Task<MeDto> GetMeAsync(CancellationToken ct)
    {
        var row = await (
                from u in db.Users
                join m in db.Memberships on u.Id equals m.UserId
                join o in db.Organizations on m.OrganizationId equals o.Id
                join r in db.Roles on m.RoleId equals r.Id
                where u.Id == context.UserId
                select new { u.Email, u.DisplayName, o.Name, Role = r.Name })
            .AsNoTracking()
            .FirstAsync(ct);

        // As capabilities enviadas ao frontend servem APENAS para UX (esconder botões).
        // A autorização real permanece no backend.
        return new MeDto(context.UserId, row.Email, row.DisplayName, context.OrganizationId, row.Name, context.TeamId, row.Role,
            context.Capabilities.ToDictionary(kv => kv.Key, kv => kv.Value.ToString().ToLowerInvariant()));
    }

    /// <summary>Usuários para os quais o ator pode atribuir leads (respeita o escopo de leads.assign).</summary>
    public async Task<IReadOnlyList<UserSummaryDto>> ListAssignableAsync(CancellationToken ct)
    {
        var scope = context.ScopeOf(Capabilities.LeadsAssign);
        var query =
            from m in db.Memberships
            join u in db.Users on m.UserId equals u.Id
            join r in db.Roles on m.RoleId equals r.Id
            where m.Status == MembershipStatus.Active && u.IsActive
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
