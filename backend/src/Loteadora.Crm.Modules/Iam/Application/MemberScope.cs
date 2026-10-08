using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Loteadora.Crm.Modules.Iam.Application;

/// <summary>
/// Resolve sobre QUAIS usuários o ator pode agir, dado o escopo de uma capability
/// (ex.: ver a agenda de alguém, atribuir tarefa). Usuário fora do escopo = 404.
/// </summary>
public sealed class MemberScope(AppDbContext db, ICurrentActor actor)
{
    public IQueryable<Guid> UserIds(CapabilityScope scope)
    {
        var members = db.Memberships.Where(m => m.Status == MembershipStatus.Active);
        return scope switch
        {
            CapabilityScope.Org => members.Select(m => m.UserId),
            CapabilityScope.Team when actor.TeamId is { } teamId => members.Where(m => m.TeamId == teamId).Select(m => m.UserId),
            _ => members.Where(m => m.UserId == actor.UserId).Select(m => m.UserId),
        };
    }

    /// <summary>Exige a capability e garante que o usuário-alvo está no escopo. Retorna o id do alvo.</summary>
    public async Task<Guid> RequireUserAsync(string capability, Guid? targetUserId, CancellationToken ct)
    {
        var scope = actor.Require(capability);
        var target = targetUserId ?? actor.UserId;
        if (target == actor.UserId && actor.Kind == ActorKind.User)
            return target;

        if (!await UserIds(scope).AnyAsync(id => id == target, ct))
            throw new NotFoundException("Usuário");
        return target;
    }
}
