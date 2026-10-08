using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;

namespace Loteadora.Crm.Modules.Tasks.Domain;

public enum TaskKind
{
    FollowUp = 1,
    Call = 2,
    Message = 3,
    Document = 4,
    Other = 5,
}

public enum TaskStatus
{
    Open = 1,
    Done = 2,
    Cancelled = 3,
}

/// <summary>Tarefa/follow-up atribuída a um usuário, opcionalmente ligada a um lead.</summary>
public sealed class TaskItem : Entity, ITenantOwned, IHasTimestamps
{
    private TaskItem() { }

    public TaskItem(Guid organizationId, Guid assigneeUserId, Guid? leadId, TaskKind kind, string title, string? notes, DateTimeOffset dueAt, ActorKind createdByKind, Guid? createdById)
    {
        OrganizationId = organizationId;
        AssigneeUserId = assigneeUserId;
        LeadId = leadId;
        Kind = kind;
        CreatedByKind = createdByKind;
        CreatedById = createdById;
        Update(title, notes, dueAt, kind);
        Raise(new TaskCreated(organizationId, Id, assigneeUserId, leadId, dueAt));
    }

    public Guid OrganizationId { get; private set; }
    public Guid AssigneeUserId { get; private set; }
    public Guid? LeadId { get; private set; }
    public TaskKind Kind { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Notes { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public TaskStatus Status { get; private set; } = TaskStatus.Open;
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CompletedById { get; private set; }
    public ActorKind CreatedByKind { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Update(string title, string? notes, DateTimeOffset dueAt, TaskKind kind)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("task.title_required", "Informe o título da tarefa.");
        Title = Guard.MaxLength(title.Trim(), 160, "Título")!;
        Notes = Guard.MaxLength(notes?.Trim(), 2000, "Observações");
        DueAt = dueAt.ToUniversalTime();
        Kind = kind;
    }

    public void Reassign(Guid assigneeUserId) => AssigneeUserId = assigneeUserId;

    public void Complete(Guid? byUserId, DateTimeOffset now)
    {
        if (Status != TaskStatus.Open)
            throw new DomainException("task.not_open", "A tarefa não está aberta.");
        Status = TaskStatus.Done;
        CompletedAt = now;
        CompletedById = byUserId;
        Raise(new TaskCompleted(OrganizationId, Id, AssigneeUserId, LeadId));
    }

    public void Reopen()
    {
        if (Status == TaskStatus.Open)
            return;
        Status = TaskStatus.Open;
        CompletedAt = null;
        CompletedById = null;
    }

    public void Cancel()
    {
        if (Status != TaskStatus.Open)
            throw new DomainException("task.not_open", "A tarefa não está aberta.");
        Status = TaskStatus.Cancelled;
    }
}

public sealed record TaskCreated(Guid OrganizationId, Guid TaskId, Guid AssigneeUserId, Guid? LeadId, DateTimeOffset DueAt) : IDomainEvent;

public sealed record TaskCompleted(Guid OrganizationId, Guid TaskId, Guid AssigneeUserId, Guid? LeadId) : IDomainEvent;
