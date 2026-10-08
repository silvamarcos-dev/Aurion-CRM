using System.Security.Claims;
using System.Threading.RateLimiting;
using Loteadora.Crm.Modules.Iam.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Loteadora.Crm.Api.Infrastructure;

public static class SecurityRegistration
{
    public const string AuthRateLimitPolicy = "auth";
    public const string UserRateLimitPolicy = "per-user";

    public static IServiceCollection AddCrmSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        var authPermitPerMinute = configuration.GetValue("RateLimiting:AuthPermitPerMinute", 10);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = auth.Issuer,
                    ValidAudience = auth.Audience,
                    IssuerSigningKey = AuthService.SigningKey(auth),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                };
                options.Events = new JwtBearerEvents
                {
                    // Após validar a assinatura: verifica sessão ativa e carrega permissões atuais do banco.
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal!;
                        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) ||
                            !Guid.TryParse(principal.FindFirstValue(CrmClaims.Organization), out var orgId) ||
                            !Guid.TryParse(principal.FindFirstValue(CrmClaims.Session), out var sessionId))
                        {
                            context.Fail("Token incompleto.");
                            return;
                        }

                        var sp = context.HttpContext.RequestServices;
                        var ct = context.HttpContext.RequestAborted;
                        if (!await sp.GetRequiredService<AuthService>().IsSessionActiveAsync(sessionId, ct))
                        {
                            context.Fail("Sessão revogada.");
                            return;
                        }

                        var actor = await sp.GetRequiredService<ActorLoader>().LoadAsync(userId, orgId, ct);
                        if (actor is null)
                        {
                            context.Fail("Acesso revogado.");
                            return;
                        }

                        sp.GetRequiredService<RequestContext>().InitializeUser(actor.UserId, actor.OrganizationId, actor.TeamId, actor.Capabilities);
                    },
                };
            });

        // Toda rota exige autenticação, salvo opt-out explícito (AllowAnonymous).
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Login/refresh: por IP, janela curta (proteção contra força bruta; complementa o lockout por conta).
            options.AddPolicy(AuthRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            // API geral: por usuário autenticado (ou IP).
            options.AddPolicy(UserRateLimitPolicy, http => RateLimitPartition.GetTokenBucketLimiter(
                http.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? http.Connection.RemoteIpAddress?.ToString() ?? "anon",
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 300,
                    TokensPerPeriod = 300,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        return services;
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.CacheControl = "no-store";
            await next();
        });
}
