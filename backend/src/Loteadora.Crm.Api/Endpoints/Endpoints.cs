using Loteadora.Crm.Api.Infrastructure;
using Loteadora.Crm.Modules.Crm.Application;
using Loteadora.Crm.Modules.Crm.Domain;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Pipeline.Application;

namespace Loteadora.Crm.Api.Endpoints;

public sealed record LoginRequest(string Email, string Password, string? Organization);

public sealed record RefreshRequest(string RefreshToken);

public sealed record VersionResponse(uint Version);

public sealed record CreatedResponse(Guid Id);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").AllowAnonymous().RequireRateLimiting(SecurityRegistration.AuthRateLimitPolicy).WithTags("Auth");

        auth.MapPost("/login", async (LoginRequest request, AuthService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
                return Results.Problem(statusCode: 400, title: "Informe e-mail e senha.");

            var result = await service.LoginAsync(request.Email, request.Password, request.Organization, ct);
            return result.Succeeded ? Results.Ok(result.Tokens) : Results.Problem(statusCode: 401, title: result.Error);
        });

        auth.MapPost("/refresh", async (RefreshRequest request, AuthService service, CancellationToken ct) =>
        {
            var result = await service.RefreshAsync(request.RefreshToken, ct);
            return result.Succeeded ? Results.Ok(result.Tokens) : Results.Problem(statusCode: 401, title: result.Error);
        });

        auth.MapPost("/logout", async (RefreshRequest request, AuthService service, CancellationToken ct) =>
        {
            await service.LogoutAsync(request.RefreshToken, ct);
            return Results.NoContent();
        });
    }
}

public static class DirectoryEndpoints
{
    public static void MapDirectoryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", (DirectoryService service, CancellationToken ct) => service.GetMeAsync(ct)).WithTags("Directory");
        api.MapGet("/users/assignable", (DirectoryService service, CancellationToken ct) => service.ListAssignableAsync(ct)).WithTags("Directory");
        api.MapGet("/users/in-scope", (string capability, DirectoryService service, CancellationToken ct) => service.ListInScopeAsync(capability, ct)).WithTags("Directory");
    }
}

public static class LeadEndpoints
{
    public static void MapLeadEndpoints(this RouteGroupBuilder api)
    {
        var leads = api.MapGroup("/leads").WithTags("Leads");

        leads.MapGet("/", (Guid? pipelineId, Guid? stageId, Guid? ownerUserId, LeadStatus? status, string? search, int? page, int? pageSize, LeadService service, CancellationToken ct) =>
            service.ListAsync(new LeadListQuery(pipelineId, stageId, ownerUserId, status, search, page ?? 1, pageSize ?? 50), ct));

        leads.MapPost("/", async (CreateLeadCommand cmd, LeadService service, CancellationToken ct) =>
        {
            var id = await service.CreateAsync(cmd, ct);
            return Results.Created($"/api/v1/leads/{id}", new CreatedResponse(id));
        });

        leads.MapGet("/{id:guid}", (Guid id, LeadService service, CancellationToken ct) => service.GetAsync(id, ct));

        leads.MapPut("/{id:guid}", async (Guid id, UpdateLeadCommand cmd, LeadService service, CancellationToken ct) =>
            new VersionResponse(await service.UpdateAsync(id, cmd, ct)));

        leads.MapPost("/{id:guid}/move", async (Guid id, MoveLeadCommand cmd, LeadService service, CancellationToken ct) =>
            new VersionResponse(await service.MoveAsync(id, cmd, ct)));

        leads.MapPost("/{id:guid}/assign", async (Guid id, AssignLeadCommand cmd, LeadService service, CancellationToken ct) =>
        {
            await service.AssignAsync(id, cmd, ct);
            return Results.NoContent();
        });

        leads.MapGet("/{id:guid}/history", (Guid id, LeadService service, CancellationToken ct) => service.GetHistoryAsync(id, ct));

        api.MapGet("/board", (Guid? pipelineId, LeadService service, CancellationToken ct) => service.GetBoardAsync(pipelineId, ct)).WithTags("Leads");
    }
}

public static class PipelineEndpoints
{
    public static void MapPipelineEndpoints(this RouteGroupBuilder api)
    {
        var pipelines = api.MapGroup("/pipelines").WithTags("Pipelines");

        pipelines.MapGet("/", (PipelineService service, CancellationToken ct) => service.ListAsync(ct));

        pipelines.MapPost("/{pipelineId:guid}/stages", (Guid pipelineId, AddStageCommand cmd, PipelineService service, CancellationToken ct) =>
            service.AddStageAsync(pipelineId, cmd, ct));

        pipelines.MapPut("/{pipelineId:guid}/stages/{stageId:guid}", async (Guid pipelineId, Guid stageId, UpdateStageCommand cmd, PipelineService service, CancellationToken ct) =>
        {
            await service.UpdateStageAsync(pipelineId, stageId, cmd, ct);
            return Results.NoContent();
        });

        pipelines.MapPost("/{pipelineId:guid}/stages/{stageId:guid}/archive", async (Guid pipelineId, Guid stageId, PipelineService service, CancellationToken ct) =>
        {
            await service.ArchiveStageAsync(pipelineId, stageId, ct);
            return Results.NoContent();
        });

        pipelines.MapPut("/{pipelineId:guid}/stages/order", async (Guid pipelineId, ReorderStagesCommand cmd, PipelineService service, CancellationToken ct) =>
        {
            await service.ReorderAsync(pipelineId, cmd, ct);
            return Results.NoContent();
        });
    }
}
