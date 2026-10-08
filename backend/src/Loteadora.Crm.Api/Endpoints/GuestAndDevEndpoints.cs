using Loteadora.Crm.Api.Infrastructure;
using Loteadora.Crm.Modules.DevTools;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Suggestions.Application;
using Loteadora.Crm.Modules.Suggestions.Domain;
using Loteadora.Crm.SharedKernel.Security;

namespace Loteadora.Crm.Api.Endpoints;

public sealed record RedeemAccessLinkRequest(string Token);

public sealed record EndpointInfoDto(string Method, string Route, string Group, bool Anonymous);

public static class AccessLinkEndpoints
{
    public static void MapAccessLinkEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/auth/access-link", async (RedeemAccessLinkRequest request, AccessLinkService service, CancellationToken ct) =>
            {
                var result = await service.RedeemAsync(request.Token, ct);
                return result.Succeeded ? Results.Ok(result.Tokens) : Results.Problem(statusCode: 401, title: result.Error);
            })
            .AllowAnonymous()
            .RequireRateLimiting(SecurityRegistration.AuthRateLimitPolicy)
            .WithTags("Auth");

        var links = api.MapGroup("/access-links").WithTags("Acessos externos");
        links.MapGet("/", (AccessLinkService service, CancellationToken ct) => service.ListAsync(ct));
        links.MapPost("/", (CreateAccessLinkCommand cmd, AccessLinkService service, CancellationToken ct) => service.CreateAsync(cmd, ct));
        links.MapPost("/{id:guid}/revoke", async (Guid id, AccessLinkService service, CancellationToken ct) =>
        {
            await service.RevokeAsync(id, ct);
            return Results.NoContent();
        });
    }
}

public static class SuggestionEndpoints
{
    public static void MapSuggestionEndpoints(this RouteGroupBuilder api)
    {
        var suggestions = api.MapGroup("/suggestions").WithTags("Sugestões");
        suggestions.MapGet("/", (SuggestionStatus? status, SuggestionKind? kind, SuggestionService service, CancellationToken ct) => service.ListAsync(status, kind, ct));
        suggestions.MapGet("/stats", (SuggestionService service, CancellationToken ct) => service.GetStatsAsync(ct));
        suggestions.MapGet("/{id:guid}", (Guid id, SuggestionService service, CancellationToken ct) => service.GetAsync(id, ct));
        suggestions.MapPost("/", async (CreateSuggestionCommand cmd, SuggestionService service, CancellationToken ct) =>
            Results.Ok(new CreatedResponse(await service.CreateAsync(cmd, ct))));
        suggestions.MapPost("/{id:guid}/triage", async (Guid id, TriageSuggestionCommand cmd, SuggestionService service, CancellationToken ct) =>
        {
            await service.TriageAsync(id, cmd, ct);
            return Results.NoContent();
        });
        suggestions.MapPost("/{id:guid}/comments", async (Guid id, AddCommentCommand cmd, SuggestionService service, CancellationToken ct) =>
            Results.Ok(new CreatedResponse(await service.CommentAsync(id, cmd, ct))));
    }
}

public static class DevToolsEndpoints
{
    public static void MapDevToolsEndpoints(this RouteGroupBuilder api)
    {
        var dev = api.MapGroup("/dev").WithTags("Desenvolvedor");
        dev.MapGet("/system", (DevToolsService service, CancellationToken ct) => service.GetSystemInfoAsync(ct));
        dev.MapGet("/outbox", (DevToolsService service, CancellationToken ct) => service.GetOutboxStatsAsync(ct));
        dev.MapGet("/audit", (string? action, string? entityType, long? before, int? limit, DevToolsService service, CancellationToken ct) =>
            service.GetAuditAsync(action, entityType, before, limit ?? 50, ct));
        dev.MapGet("/roles", (DevToolsService service, CancellationToken ct) => service.GetRoleMatrixAsync(ct));

        // Mapa de endpoints gerado a partir das rotas registradas (sempre em sincronia com o código).
        dev.MapGet("/endpoints", (EndpointDataSource sources, ICurrentActor actor) =>
        {
            actor.Require(Capabilities.DevTools);
            return sources.Endpoints.OfType<RouteEndpoint>()
                .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
                .Select(e => new EndpointInfoDto(
                    string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]),
                    e.RoutePattern.RawText!,
                    e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.ITagsMetadata>() is { Tags.Count: > 0 } tags ? tags.Tags[0] : "Outros",
                    e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null))
                .OrderBy(e => e.Group).ThenBy(e => e.Route).ThenBy(e => e.Method)
                .ToList();
        });
    }
}
