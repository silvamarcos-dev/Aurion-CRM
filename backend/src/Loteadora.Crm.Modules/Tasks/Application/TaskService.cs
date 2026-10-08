using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.Crm.Application;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.Modules.Tasks.Domain;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using TaskStatus = Loteadora.Crm.Modules.Tasks.Domain.TaskStatus;

namespace Loteadora.Crm.Modules.Tasks.Application;

public enum TaskDueFilter
{
    All = 0,
    Overdue = 1,
    Today = 2,
    Upcoming = 3,
}

public sealed record TaskListQuery(Guid? AssigneeUserId, Guid? LeadId, TaskStatus? Status, TaskDueFilter Due = TaskDueFilter.All, int Limit = 200);

public sealed record TaskDto(Guid Id, Guid AssigneeUserId, string AssigneeName, Guid? LeadId, string? LeadName, TaskKind Kind, string Title, string? Notes,
    DateTimeOffset DueAt, TaskStatus Status, DateTimeOffset? CompletedAt, string CreatedByKind);

public sealed record CreateTaskCommand(Guid? AssigneeUserId, Guid? LeadId, TaskKind Kind, string Title, string? Notes, DateTimeOffset DueAt);

public sealed record UpdateTaskCommand(Guid AssigneeUserId, TaskKind Kind, string Title, string? Notes, DateTimeOffset DueAt);

/// <summary>
/// Tarefas e follow-ups. Mantém Lead.NextFollowUpAt = vencimento da próxima tarefa aberta do lead
/// (na mesma transação). O fuso usado para "hoje" é o da organização.
/// </summary>
public sealed class TaskService(AppDbContext db, ICurrentActor actor, IAuditTrail audit, MemberScope members, LeadAccess leads,
    BusinessCalendar.Application.BusinessCalendarService calendars, PiiMask mask, TimeProvider clock)
{
    public async Task<IReadOnlyList<TaskDto>> ListAsync(TaskListQuery query, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.TasksRead);
        var allowed = members.UserIds(scope);
        var tasks = db.TaskItems.AsNoTracking().Where(t => allowed.Contains(t.AssigneeUserId));

        if (query.AssigneeUserId is { } assignee) tasks = tasks.Where(t => t.AssigneeUserId == assignee);
        if (query.LeadId is { } leadId)
        {
            await leads.GetVisibleAsync(leadId, ct);
            tasks = tasks.Where(t => t.LeadId == leadId);
        }
        if (query.Status is { } status) tasks = tasks.Where(t => t.Status == status);

        if (query.Due != TaskDueFilter.All)
        {
            var (todayStart, tomorrowStart) = await TodayBoundsAsync(ct);
            var now = clock.GetUtcNow();
            tasks = query.Due switch
            {
                TaskDueFilter.Overdue => tasks.Where(t => t.Status == TaskStatus.Open && t.DueAt < now),
                TaskDueFilter.Today => tasks.Where(t => t.DueAt >= todayStart && t.DueAt < tomorrowStart),
                _ => tasks.Where(t => t.DueAt >= tomorrowStart),
            };
        }

        var items = await (
                from t in tasks
                join u in db.Users on t.AssigneeUserId equals u.Id
                join l in db.Leads on t.LeadId equals (Guid?)l.Id into ls
                from l in ls.DefaultIfEmpty()
                join c in db.Contacts on l.ContactId equals c.Id into cs
                from c in cs.DefaultIfEmpty()
                orderby t.Status, t.DueAt
                select new TaskDto(t.Id, t.AssigneeUserId, u.DisplayName, t.LeadId, c != null ? c.FullName : null, t.Kind, t.Title, t.Notes, t.DueAt, t.Status,
                    t.CompletedAt, t.CreatedByKind.ToString()))
            .Take(Math.Clamp(query.Limit, 1, 500))
            .ToListAsync(ct);

        return mask.IsMasking
            ? items.Select(t => t with { LeadName = mask.OptionalName(t.LeadName), Notes = mask.FreeText(t.Notes) }).ToList()
            : items;
    }

    public async Task<Guid> CreateAsync(CreateTaskCommand cmd, CancellationToken ct)
    {
        var lead = cmd.LeadId is { } leadId ? await leads.GetVisibleAsync(leadId, ct) : null;
        var assignee = await members.RequireUserAsync(Capabilities.TasksManage, cmd.AssigneeUserId ?? lead?.OwnerUserId, ct);

        var task = new TaskItem(actor.OrganizationId, assignee, lead?.Id, cmd.Kind, cmd.Title, cmd.Notes, cmd.DueAt, actor.Kind,
            actor.Kind == ActorKind.System ? null : actor.UserId);
        db.TaskItems.Add(task);
        audit.Record("task.created", lead is null ? "task" : "lead", lead?.Id ?? task.Id, new { taskId = task.Id, assigneeUserId = assignee, kind = cmd.Kind.ToString(), dueAt = task.DueAt });

        if (lead is not null)
            await SyncLeadFollowUpAsync(lead.Id, pending: task, ct);

        await db.SaveChangesAsync(ct);
        return task.Id;
    }

    public async Task UpdateAsync(Guid id, UpdateTaskCommand cmd, CancellationToken ct)
    {
        var task = await LoadForManageAsync(id, ct);
        if (cmd.AssigneeUserId != task.AssigneeUserId)
            task.Reassign(await members.RequireUserAsync(Capabilities.TasksManage, cmd.AssigneeUserId, ct));

        task.Update(cmd.Title, cmd.Notes, cmd.DueAt, cmd.Kind);
        audit.Record("task.updated", task.LeadId is null ? "task" : "lead", task.LeadId ?? task.Id, new { taskId = task.Id, assigneeUserId = task.AssigneeUserId, dueAt = task.DueAt });
        await SyncAndSaveAsync(task, ct);
    }

    public async Task CompleteAsync(Guid id, CancellationToken ct)
    {
        var task = await LoadForManageAsync(id, ct);
        var now = clock.GetUtcNow();
        task.Complete(actor.Kind == ActorKind.System ? null : actor.UserId, now);
        if (task.LeadId is { } leadId && task.Kind is TaskKind.FollowUp or TaskKind.Call or TaskKind.Message)
            await leads.RegisterContactAsync(leadId, now, ct);

        audit.Record("task.completed", task.LeadId is null ? "task" : "lead", task.LeadId ?? task.Id, new { taskId = task.Id });
        await SyncAndSaveAsync(task, ct);
    }

    public async Task ReopenAsync(Guid id, CancellationToken ct)
    {
        var task = await LoadForManageAsync(id, ct);
        task.Reopen();
        audit.Record("task.reopened", task.LeadId is null ? "task" : "lead", task.LeadId ?? task.Id, new { taskId = task.Id });
        await SyncAndSaveAsync(task, ct);
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var task = await LoadForManageAsync(id, ct);
        task.Cancel();
        audit.Record("task.cancelled", task.LeadId is null ? "task" : "lead", task.LeadId ?? task.Id, new { taskId = task.Id });
        await SyncAndSaveAsync(task, ct);
    }

    private async Task SyncAndSaveAsync(TaskItem task, CancellationToken ct)
    {
        if (task.LeadId is { } leadId)
            await SyncLeadFollowUpAsync(leadId, pending: task, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Recalcula o próximo follow-up do lead considerando o estado em memória da tarefa alterada.</summary>
    private async Task SyncLeadFollowUpAsync(Guid leadId, TaskItem pending, CancellationToken ct)
    {
        var others = await db.TaskItems.AsNoTracking()
            .Where(t => t.LeadId == leadId && t.Status == TaskStatus.Open && t.Id != pending.Id)
            .Select(t => (DateTimeOffset?)t.DueAt)
            .MinAsync(ct);

        DateTimeOffset? next = pending.Status == TaskStatus.Open
            ? (others is null || pending.DueAt < others ? pending.DueAt : others)
            : others;

        await leads.SetNextFollowUpAsync(leadId, next, ct);
    }

    private async Task<TaskItem> LoadForManageAsync(Guid id, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.TasksManage);
        var allowed = members.UserIds(scope);
        return await db.TaskItems.FirstOrDefaultAsync(t => t.Id == id && allowed.Contains(t.AssigneeUserId), ct)
               ?? throw new NotFoundException("Tarefa");
    }

    private async Task<(DateTimeOffset TodayStart, DateTimeOffset TomorrowStart)> TodayBoundsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var calendar = await calendars.LoadAsync(DateOnly.FromDateTime(now.UtcDateTime), DateOnly.FromDateTime(now.UtcDateTime), ct);
        var today = NodaTime.Instant.FromDateTimeOffset(now).InZone(calendar.Zone).Date;
        return (today.AtStartOfDayInZone(calendar.Zone).ToInstant().ToDateTimeOffset(), today.PlusDays(1).AtStartOfDayInZone(calendar.Zone).ToInstant().ToDateTimeOffset());
    }
}
