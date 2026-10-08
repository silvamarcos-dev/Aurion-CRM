using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.Audit.Domain;
using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Loteadora.Crm.Modules.Iam.Application;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = "loteadora-crm";
    public string Audience { get; set; } = "loteadora-crm-api";

    /// <summary>Chave HMAC (≥ 32 bytes). Em produção vem do secret manager — nunca do repositório.</summary>
    public string SigningKey { get; set; } = "";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Janela em que o reuso de um refresh token recém-rotacionado é tratado como corrida benigna
    /// (requisições paralelas do mesmo navegador) e não como roubo. Fora dela, a família é revogada.
    /// </summary>
    public TimeSpan RefreshReuseGracePeriod { get; set; } = TimeSpan.FromSeconds(20);
}

public static class CrmClaims
{
    public const string Organization = "org";
    public const string Session = "sid";
}

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public sealed record LoginResult(bool Succeeded, TokenPair? Tokens, string? Error)
{
    public static LoginResult Fail(string error) => new(false, null, error);
}

public sealed class AuthService(
    AppDbContext db,
    ActorLoader actorLoader,
    IPasswordHasher<User> hasher,
    IOptions<AuthOptions> options,
    TimeProvider clock,
    ICorrelationIdAccessor correlation)
{
    private const string InvalidCredentials = "Credenciais inválidas.";
    private readonly AuthOptions _options = options.Value;

    public async Task<LoginResult> LoginAsync(string email, string password, string? organizationSlug, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var normalized = User.NormalizeEmail(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);

        if (user is null)
        {
            // Custo equivalente para não revelar existência do e-mail por tempo de resposta.
            hasher.HashPassword(new User("timing@invalid", "x"), password);
            return LoginResult.Fail(InvalidCredentials);
        }

        if (!user.IsActive || user.IsGuest || user.IsLockedOut(now))
            return LoginResult.Fail(InvalidCredentials);

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedLogin(now);
            await RecordAuthAuditForAllOrgsAsync(user.Id, AuditActions.UserLoginFailed, ct);
            await db.SaveChangesAsync(ct);
            return LoginResult.Fail(InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.SetPasswordHash(hasher.HashPassword(user, password));

        var memberships = await (
                from m in db.Memberships.IgnoreQueryFilters()
                join o in db.Organizations on m.OrganizationId equals o.Id
                where m.UserId == user.Id && m.Status == MembershipStatus.Active && (m.ExpiresAt == null || m.ExpiresAt > now) && o.IsActive
                select new { m.OrganizationId, o.Slug })
            .ToListAsync(ct);

        var membership = organizationSlug is null
            ? (memberships.Count == 1 ? memberships[0] : null)
            : memberships.FirstOrDefault(m => m.Slug == organizationSlug);

        if (membership is null)
            return LoginResult.Fail(memberships.Count > 1 ? "Informe a organização." : InvalidCredentials);

        user.RegisterSuccessfulLogin();
        var tokens = IssueTokens(user.Id, membership.OrganizationId, familyId: Guid.CreateVersion7(), now);
        AddAudit(membership.OrganizationId, user.Id, AuditActions.UserLoggedIn, user.Id);
        await db.SaveChangesAsync(ct);
        return new LoginResult(true, tokens, null);
    }

    /// <summary>Rotação de refresh token. Reuso de token já rotacionado revoga a família inteira.</summary>
    public async Task<LoginResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = HashToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null)
            return LoginResult.Fail("Sessão inválida.");

        if (!stored.IsActive(now))
        {
            var withinGrace = stored.RevokedAt is { } revokedAt && now - revokedAt <= _options.RefreshReuseGracePeriod;
            if (stored.ReplacedById is not null && !withinGrace)
            {
                await RevokeFamilyAsync(stored.FamilyId, now, ct);
                AddAudit(stored.OrganizationId, stored.UserId, AuditActions.RefreshTokenReuse, stored.UserId);
                await db.SaveChangesAsync(ct);
            }
            return LoginResult.Fail("Sessão expirada.");
        }

        var actorStillValid = await actorLoader.LoadAsync(stored.UserId, stored.OrganizationId, ct);
        if (actorStillValid is null)
        {
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
            await db.SaveChangesAsync(ct);
            return LoginResult.Fail("Acesso revogado.");
        }

        var tokens = IssueTokens(stored.UserId, stored.OrganizationId, stored.FamilyId, now, out var newToken);
        stored.Revoke(now, newToken.Id);
        await db.SaveChangesAsync(ct);
        return new LoginResult(true, tokens, null);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = HashToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null)
            return;

        await RevokeFamilyAsync(stored.FamilyId, clock.GetUtcNow(), ct);
        AddAudit(stored.OrganizationId, stored.UserId, AuditActions.UserLoggedOut, stored.UserId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Usado pela validação do JWT: a sessão (família) ainda está ativa?</summary>
    public Task<bool> IsSessionActiveAsync(Guid familyId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.RefreshTokens.AnyAsync(t => t.FamilyId == familyId && t.RevokedAt == null && t.ExpiresAt > now, ct);
    }

    private TokenPair IssueTokens(Guid userId, Guid organizationId, Guid familyId, DateTimeOffset now) =>
        IssueTokens(userId, organizationId, familyId, now, out _);

    /// <summary>Emite uma nova sessão para um usuário já validado por outro fator (ex.: link de acesso). Não salva.</summary>
    internal TokenPair IssueSession(Guid userId, Guid organizationId, DateTimeOffset now) =>
        IssueTokens(userId, organizationId, Guid.CreateVersion7(), now);

    private TokenPair IssueTokens(Guid userId, Guid organizationId, Guid familyId, DateTimeOffset now, out RefreshToken refreshEntity)
    {
        var accessExpires = now + _options.AccessTokenLifetime;
        var handler = new JsonWebTokenHandler();
        var accessToken = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = accessExpires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(CrmClaims.Organization, organizationId.ToString()),
                new Claim(CrmClaims.Session, familyId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            SigningCredentials = new SigningCredentials(SigningKey(_options), SecurityAlgorithms.HmacSha256),
        });

        var rawRefresh = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        var refreshExpires = now + _options.RefreshTokenLifetime;
        refreshEntity = new RefreshToken(userId, organizationId, familyId, HashToken(rawRefresh), refreshExpires, now);
        db.RefreshTokens.Add(refreshEntity);

        return new TokenPair(accessToken, accessExpires, rawRefresh, refreshExpires);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        var family = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var token in family)
            token.Revoke(now);
    }

    private async Task RecordAuthAuditForAllOrgsAsync(Guid userId, string action, CancellationToken ct)
    {
        var orgIds = await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.UserId == userId)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);
        foreach (var orgId in orgIds)
            AddAudit(orgId, userId, action, userId);
    }

    private void AddAudit(Guid organizationId, Guid userId, string action, Guid entityId) =>
        db.AuditEvents.Add(new AuditEvent(organizationId, clock.GetUtcNow(), ActorKind.User, userId, action, "user", entityId, "{}", correlation.CorrelationId));

    public static SymmetricSecurityKey SigningKey(AuthOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(options.SigningKey);
        if (bytes.Length < 32)
            throw new InvalidOperationException("Auth:SigningKey deve ter pelo menos 32 bytes.");
        return new SymmetricSecurityKey(bytes);
    }

    private static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
