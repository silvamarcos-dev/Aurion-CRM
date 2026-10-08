using System.Security.Cryptography;
using System.Text;
using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Loteadora.Crm.Modules.Iam.Application;

public sealed record CreateAccessLinkCommand(string Label, string Role, int ExpiresInDays, string? Note);

/// <summary>Resposta da criação: o token em claro aparece SOMENTE aqui.</summary>
public sealed record CreatedAccessLinkDto(Guid Id, string Token, DateTimeOffset ExpiresAt);

public sealed record AccessLinkDto(Guid Id, string Label, string Role, string? Note, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt, DateTimeOffset? LastUsedAt, int UseCount, string CreatedByName, string Status);

/// <summary>
/// Acessos externos por link (Visitante / Desenvolvedor). Cada link cria um usuário convidado próprio,
/// com vínculo que expira junto com o link. Convidados não têm senha e nunca veem dados pessoais completos
/// (os papéis de convidado não possuem a capability pii.view).
/// </summary>
public sealed class AccessLinkService(AppDbContext db, ICurrentActor actor, RequestContext context, IAuditTrail audit, AuthService auth, TimeProvider clock)
{
    public const int MaxDays = 30;

    public async Task<CreatedAccessLinkDto> CreateAsync(CreateAccessLinkCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.UsersManage);
        if (!SystemRoles.GuestRoles.Contains(cmd.Role))
            throw new DomainException("access_link.role_invalid", "Links de acesso só podem conceder os papéis Visitante ou Desenvolvedor.");
        if (cmd.ExpiresInDays is < 1 or > MaxDays)
            throw new DomainException("access_link.expiry_invalid", $"A validade deve ser de 1 a {MaxDays} dias.");

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == cmd.Role && r.IsSystem, ct)
                   ?? throw new DomainException("access_link.role_missing", "Papel não encontrado nesta organização.");

        var now = clock.GetUtcNow();
        var expiresAt = now.AddDays(cmd.ExpiresInDays);
        var guest = User.CreateGuest($"{cmd.Label.Trim()} ({cmd.Role})");
        var membership = new Membership(actor.OrganizationId, guest.Id, role.Id, teamId: null);
        membership.ExpireAt(expiresAt);

        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var link = new AccessLink(actor.OrganizationId, guest.Id, cmd.Role, cmd.Label, cmd.Note, Hash(token), expiresAt, actor.UserId);

        db.Users.Add(guest);
        db.Memberships.Add(membership);
        db.AccessLinks.Add(link);
        audit.Record("access_link.created", "access_link", link.Id, new { role = cmd.Role, link.ExpiresAt, guestUserId = guest.Id });
        await db.SaveChangesAsync(ct);
        return new CreatedAccessLinkDto(link.Id, token, expiresAt);
    }

    public async Task<IReadOnlyList<AccessLinkDto>> ListAsync(CancellationToken ct)
    {
        actor.Require(Capabilities.UsersManage);
        var now = clock.GetUtcNow();
        var rows = await (
                from l in db.AccessLinks.AsNoTracking()
                join u in db.Users on l.CreatedById equals u.Id
                orderby l.CreatedAt descending
                select new { l, CreatedBy = u.DisplayName })
            .Take(200)
            .ToListAsync(ct);

        return rows.Select(x => new AccessLinkDto(x.l.Id, x.l.Label, x.l.RoleName, x.l.Note, x.l.CreatedAt, x.l.ExpiresAt, x.l.RevokedAt,
            x.l.LastUsedAt, x.l.UseCount, x.CreatedBy,
            x.l.RevokedAt is not null ? "Revoked" : x.l.ExpiresAt <= now ? "Expired" : "Active")).ToList();
    }

    /// <summary>Revoga o link: encerra o vínculo e todas as sessões do convidado imediatamente.</summary>
    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        actor.Require(Capabilities.UsersManage);
        var now = clock.GetUtcNow();
        var link = await db.AccessLinks.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Link de acesso");
        link.Revoke(now);

        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == link.GuestUserId, ct);
        membership?.Revoke();
        foreach (var token in await db.RefreshTokens.Where(t => t.UserId == link.GuestUserId && t.RevokedAt == null).ToListAsync(ct))
            token.Revoke(now);

        audit.Record("access_link.revoked", "access_link", link.Id, new { guestUserId = link.GuestUserId });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Troca o token do link por uma sessão (requisição anônima). Mensagem genérica em qualquer falha.</summary>
    public async Task<LoginResult> RedeemAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            return LoginResult.Fail("Link inválido ou expirado.");

        var now = clock.GetUtcNow();
        var hash = Hash(token);
        var link = await db.AccessLinks.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.TokenHash == hash, ct);
        if (link is null || !link.IsUsable(now))
            return LoginResult.Fail("Link inválido ou expirado.");

        // A partir daqui operamos no tenant do link, como ator de sistema.
        context.InitializeSystem(link.OrganizationId);

        var membershipActive = await db.Memberships.AnyAsync(m => m.UserId == link.GuestUserId && m.Status == MembershipStatus.Active
                                                                  && (m.ExpiresAt == null || m.ExpiresAt > now), ct);
        if (!membershipActive)
            return LoginResult.Fail("Link inválido ou expirado.");

        link.RegisterUse(now);
        var tokens = auth.IssueSession(link.GuestUserId, link.OrganizationId, now);
        db.AuditEvents.Add(new Audit.Domain.AuditEvent(link.OrganizationId, now, ActorKind.User, link.GuestUserId, "access_link.redeemed", "access_link", link.Id, "{}", null));
        await db.SaveChangesAsync(ct);
        return new LoginResult(true, tokens, null);
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
