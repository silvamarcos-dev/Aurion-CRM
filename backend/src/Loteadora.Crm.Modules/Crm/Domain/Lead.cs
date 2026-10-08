using Loteadora.Crm.Modules.Pipeline.Domain;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;

namespace Loteadora.Crm.Modules.Crm.Domain;

public enum LeadStatus
{
    Open = 1,
    Won = 2,
    Lost = 3,
    Archived = 4,
}

public enum PurchaseTimeframe
{
    Unknown = 0,
    Immediate = 1,
    UpTo3Months = 2,
    UpTo6Months = 3,
    UpTo12Months = 4,
    Over12Months = 5,
}

/// <summary>Interesse comercial de um Contact acompanhado pelo funil.</summary>
public sealed class Lead : Entity, ITenantOwned, IHasTimestamps
{
    private Lead() { }

    public Lead(
        Guid organizationId,
        Guid contactId,
        Guid ownerUserId,
        PipelineStage initialStage,
        string? source,
        string? campaign,
        DateTimeOffset enteredAt)
    {
        if (initialStage.OrganizationId != organizationId)
            throw new DomainException("lead.stage_tenant_mismatch", "Etapa inválida.");

        OrganizationId = organizationId;
        ContactId = contactId;
        OwnerUserId = ownerUserId;
        PipelineId = initialStage.PipelineId;
        StageId = initialStage.Id;
        Status = StatusFor(initialStage.Kind);
        Source = Guard.MaxLength(Trim(source), 80, "Origem");
        Campaign = Guard.MaxLength(Trim(campaign), 120, "Campanha");
        EnteredAt = enteredAt;
        StageEnteredAt = enteredAt;

        Raise(new LeadCreated(organizationId, Id, ownerUserId, initialStage.Id));
    }

    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid PipelineId { get; private set; }
    public Guid StageId { get; private set; }
    public LeadStatus Status { get; private set; }

    public string? Source { get; private set; }
    public string? Campaign { get; private set; }
    public string? Interest { get; private set; }
    public Guid? DevelopmentId { get; private set; }
    public Guid? LotId { get; private set; }
    public long? BudgetMinCents { get; private set; }
    public long? BudgetMaxCents { get; private set; }
    public PurchaseTimeframe Timeframe { get; private set; }
    public string? Region { get; private set; }
    public string? Notes { get; private set; }
    public List<string> Tags { get; private set; } = [];

    /// <summary>Score calculado (Fase de qualificação). Sempre acompanhado de fatores explicáveis.</summary>
    public int? Score { get; private set; }

    public string? LostReason { get; private set; }
    public DateTimeOffset EnteredAt { get; private set; }
    public DateTimeOffset StageEnteredAt { get; private set; }
    public DateTimeOffset? LastContactAt { get; private set; }
    public DateTimeOffset? NextFollowUpAt { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Token de concorrência otimista (xmin do PostgreSQL).</summary>
    public uint Version { get; private set; }

    public void UpdateDetails(
        string? interest,
        Guid? developmentId,
        Guid? lotId,
        long? budgetMinCents,
        long? budgetMaxCents,
        PurchaseTimeframe timeframe,
        string? region,
        string? notes,
        IEnumerable<string>? tags,
        DateTimeOffset? nextFollowUpAt)
    {
        if (budgetMinCents is < 0 || budgetMaxCents is < 0 ||
            (budgetMinCents is { } min && budgetMaxCents is { } max && min > max))
            throw new DomainException("lead.budget_invalid", "Faixa de investimento inválida.");

        Interest = Guard.MaxLength(Trim(interest), 500, "Interesse");
        DevelopmentId = developmentId;
        LotId = lotId;
        BudgetMinCents = budgetMinCents;
        BudgetMaxCents = budgetMaxCents;
        Timeframe = timeframe;
        Region = Guard.MaxLength(Trim(region), 120, "Região");
        Notes = Guard.MaxLength(Trim(notes), 4000, "Observações");
        Tags = tags?.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length is > 0 and <= 40).Distinct().Take(30).ToList() ?? Tags;
        NextFollowUpAt = nextFollowUpAt;
    }

    public LeadStageTransition MoveTo(PipelineStage target, ActorKind actorKind, Guid? actorId, string? reason, DateTimeOffset now)
    {
        if (target.OrganizationId != OrganizationId || target.PipelineId != PipelineId)
            throw new DomainException("lead.stage_invalid", "A etapa não pertence ao pipeline deste lead.");
        if (target.IsArchived)
            throw new DomainException("lead.stage_archived", "Etapa arquivada.");
        if (target.Id == StageId)
            throw new DomainException("lead.stage_unchanged", "O lead já está nesta etapa.");
        if (target.Kind == StageKind.Lost && string.IsNullOrWhiteSpace(reason))
            throw new DomainException("lead.lost_reason_required", "Informe o motivo da perda.");

        var transition = new LeadStageTransition(OrganizationId, Id, StageId, target.Id, actorKind, actorId, Trim(reason), now);

        var from = StageId;
        StageId = target.Id;
        StageEnteredAt = now;
        Status = StatusFor(target.Kind);
        LostReason = target.Kind == StageKind.Lost ? Guard.MaxLength(Trim(reason), 500, "Motivo") : null;

        Raise(new LeadStageChanged(OrganizationId, Id, from, target.Id, actorKind, actorId));
        return transition;
    }

    public void AssignTo(Guid newOwnerUserId, ActorKind actorKind, Guid? actorId)
    {
        if (newOwnerUserId == OwnerUserId)
            return;

        var previous = OwnerUserId;
        OwnerUserId = newOwnerUserId;
        Raise(new LeadAssigned(OrganizationId, Id, previous, newOwnerUserId, actorKind, actorId));
    }

    public void RegisterContact(DateTimeOffset at)
    {
        if (LastContactAt is null || at > LastContactAt)
            LastContactAt = at;
    }

    private static LeadStatus StatusFor(StageKind kind) => kind switch
    {
        StageKind.Won => LeadStatus.Won,
        StageKind.Lost => LeadStatus.Lost,
        _ => LeadStatus.Open,
    };

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Histórico imutável de movimentações no funil.</summary>
public sealed class LeadStageTransition : Entity, ITenantOwned
{
    private LeadStageTransition() { }

    internal LeadStageTransition(Guid organizationId, Guid leadId, Guid fromStageId, Guid toStageId, ActorKind actorKind, Guid? actorId, string? reason, DateTimeOffset occurredAt)
    {
        OrganizationId = organizationId;
        LeadId = leadId;
        FromStageId = fromStageId;
        ToStageId = toStageId;
        ActorKind = actorKind;
        ActorId = actorId;
        Reason = reason;
        OccurredAt = occurredAt;
    }

    public Guid OrganizationId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid FromStageId { get; private set; }
    public Guid ToStageId { get; private set; }
    public ActorKind ActorKind { get; private set; }
    public Guid? ActorId { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

public sealed record LeadCreated(Guid OrganizationId, Guid LeadId, Guid OwnerUserId, Guid StageId) : IDomainEvent;

public sealed record LeadStageChanged(Guid OrganizationId, Guid LeadId, Guid FromStageId, Guid ToStageId, ActorKind ActorKind, Guid? ActorId) : IDomainEvent;

public sealed record LeadAssigned(Guid OrganizationId, Guid LeadId, Guid PreviousOwnerUserId, Guid NewOwnerUserId, ActorKind ActorKind, Guid? ActorId) : IDomainEvent;
