using System.Text.Json;
using Loteadora.Crm.Modules.Audit.Domain;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Security;

namespace Loteadora.Crm.Modules.Audit;

/// <summary>
/// Registra eventos de auditoria na MESMA transação da operação de negócio
/// (gravados no SaveChanges do AppDbContext).
/// </summary>
public interface IAuditTrail
{
    void Record(string action, string entityType, Guid? entityId, object? data = null);
}

internal sealed class AuditTrail(AppDbContext db, ICurrentActor actor, TimeProvider clock, ICorrelationIdAccessor correlation) : IAuditTrail
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entityType, Guid? entityId, object? data = null)
    {
        if (!actor.IsAuthenticated)
            throw new InvalidOperationException("Auditoria requer um ator autenticado ou de sistema.");

        db.AuditEvents.Add(new AuditEvent(
            actor.OrganizationId,
            clock.GetUtcNow(),
            actor.Kind,
            actor.Kind == ActorKind.System ? null : actor.UserId,
            action,
            entityType,
            entityId,
            data is null ? "{}" : JsonSerializer.Serialize(data, JsonOptions),
            correlation.CorrelationId));
    }
}

public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}

public static class AuditActions
{
    public const string LeadCreated = "lead.created";
    public const string LeadUpdated = "lead.updated";
    public const string LeadViewed = "lead.viewed";
    public const string LeadAssigned = "lead.assigned";
    public const string LeadStageChanged = "lead.stage_changed";
    public const string ContactUpdated = "contact.updated";
    public const string PipelineConfigured = "pipeline.configured";
    public const string UserLoggedIn = "auth.login";
    public const string UserLoginFailed = "auth.login_failed";
    public const string UserLoggedOut = "auth.logout";
    public const string RefreshTokenReuse = "auth.refresh_reuse_detected";
}
