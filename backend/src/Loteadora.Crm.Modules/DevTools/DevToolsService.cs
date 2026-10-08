using System.Reflection;
using Loteadora.Crm.Modules.Crm.Application;
using Loteadora.Crm.Modules.Outbox;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Loteadora.Crm.Modules.DevTools;

public sealed record SystemInfoDto(string Environment, string AppVersion, string Runtime, DateTimeOffset ServerTimeUtc, string OrganizationTimeZone,
    string DatabaseVersion, IReadOnlyList<string> AppliedMigrations, IReadOnlyList<string> PendingMigrations);

public sealed record OutboxFailureDto(Guid Id, string Type, int Attempts, string? LastError, DateTimeOffset OccurredAt, DateTimeOffset AvailableAt, bool DeadLettered);

public sealed record OutboxStatsDto(int Pending, int Retrying, int DeadLettered, int ProcessedLast24h, double? OldestPendingSeconds, IReadOnlyList<OutboxFailureDto> RecentFailures);

public sealed record AuditEntryDto(long Sequence, DateTimeOffset OccurredAt, string ActorKind, Guid? ActorId, string? ActorName, string Action, string EntityType, Guid? EntityId, string Data, string? CorrelationId);

public sealed record RoleMatrixDto(IReadOnlyList<string> Capabilities, IReadOnlyList<RoleGrantsDto> Roles);

public sealed record RoleGrantsDto(string Role, bool IsSystem, IReadOnlyDictionary<string, string> Grants);

/// <summary>
/// Painel do desenvolvedor (somente leitura). Mostra informações técnicas do tenant corrente —
/// nunca dados pessoais completos (o papel Desenvolvedor não possui pii.view).
/// </summary>
public sealed class DevToolsService(AppDbContext db, ICurrentActor actor, IHostEnvironment env, PiiMask mask, TimeProvider clock)
{
    public async Task<SystemInfoDto> GetSystemInfoAsync(CancellationToken ct)
    {
        actor.Require(Capabilities.DevTools);
        var assembly = typeof(DevToolsService).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "dev";
        var timeZone = await db.CalendarSettings.Select(s => s.TimeZone).FirstOrDefaultAsync(ct) ?? "America/Sao_Paulo";
        var dbVersion = await db.Database.SqlQueryRaw<string>("SELECT split_part(version(), ',', 1) AS \"Value\"").FirstAsync(ct);

        return new SystemInfoDto(env.EnvironmentName, version, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, clock.GetUtcNow(), timeZone,
            dbVersion, (await db.Database.GetAppliedMigrationsAsync(ct)).ToList(), (await db.Database.GetPendingMigrationsAsync(ct)).ToList());
    }

    public async Task<OutboxStatsDto> GetOutboxStatsAsync(CancellationToken ct)
    {
        actor.Require(Capabilities.DevTools);
        var now = clock.GetUtcNow();
        var org = actor.OrganizationId;
        var mine = db.OutboxMessages.AsNoTracking().Where(m => m.OrganizationId == org);
        var unprocessed = mine.Where(m => m.ProcessedAt == null);

        var pending = await unprocessed.CountAsync(m => m.Attempts == 0, ct);
        var retrying = await unprocessed.CountAsync(m => m.Attempts > 0 && m.Attempts < OutboxMessage.MaxAttempts, ct);
        var dead = await unprocessed.CountAsync(m => m.Attempts >= OutboxMessage.MaxAttempts, ct);
        var since = now.AddDays(-1);
        var processed = await mine.CountAsync(m => m.ProcessedAt != null && m.ProcessedAt >= since, ct);
        var oldest = await unprocessed.OrderBy(m => m.OccurredAt).Select(m => (DateTimeOffset?)m.OccurredAt).FirstOrDefaultAsync(ct);
        var failures = await unprocessed.Where(m => m.Attempts > 0).OrderByDescending(m => m.OccurredAt).Take(20)
            .Select(m => new OutboxFailureDto(m.Id, m.Type, m.Attempts, m.LastError, m.OccurredAt, m.AvailableAt, m.Attempts >= OutboxMessage.MaxAttempts))
            .ToListAsync(ct);

        return new OutboxStatsDto(pending, retrying, dead, processed, oldest is null ? null : (now - oldest.Value).TotalSeconds,
            failures.Select(f => f with { Type = f.Type[(f.Type.LastIndexOf('.') + 1)..] }).ToList());
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetAuditAsync(string? action, string? entityType, long? beforeSequence, int limit, CancellationToken ct)
    {
        actor.Require(Capabilities.AuditRead);
        var query = db.AuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action.StartsWith(action));
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(a => a.EntityType == entityType);
        if (beforeSequence is { } before) query = query.Where(a => a.Sequence < before);

        var rows = await (
                from a in query
                join u in db.Users on a.ActorId equals (Guid?)u.Id into users
                from u in users.DefaultIfEmpty()
                orderby a.Sequence descending
                select new AuditEntryDto(a.Sequence, a.OccurredAt, a.ActorKind.ToString(), a.ActorId, u != null ? u.DisplayName : null, a.Action, a.EntityType, a.EntityId, a.Data, a.CorrelationId))
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        return rows.Select(r => r with { Data = mask.AuditData(r.Data) }).ToList();
    }

    public async Task<RoleMatrixDto> GetRoleMatrixAsync(CancellationToken ct)
    {
        actor.Require(Capabilities.DevTools);
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        return new RoleMatrixDto(
            Capabilities.All.OrderBy(c => c).ToList(),
            roles.Select(r => new RoleGrantsDto(r.Name, r.IsSystem, r.Capabilities.ToDictionary(c => c.Capability, c => c.Scope.ToString()))).ToList());
    }
}
