using Loteadora.Crm.Modules.Crm.Domain;

namespace Loteadora.Crm.Modules.Crm.Application;

public sealed record CreateLeadCommand(
    string FullName,
    string? Phone,
    string? Email,
    string? Source,
    string? Campaign,
    string? Interest,
    Guid? StageId,
    Guid? OwnerUserId);

public sealed record UpdateLeadCommand(
    string FullName,
    string? Phone,
    string? Email,
    string? Interest,
    Guid? DevelopmentId,
    Guid? LotId,
    long? BudgetMinCents,
    long? BudgetMaxCents,
    PurchaseTimeframe Timeframe,
    string? Region,
    string? Notes,
    IReadOnlyList<string>? Tags,
    uint ExpectedVersion);

public sealed record MoveLeadCommand(Guid StageId, string? Reason, uint ExpectedVersion);

public sealed record AssignLeadCommand(Guid OwnerUserId);

public sealed record LeadListQuery(
    Guid? PipelineId,
    Guid? StageId,
    Guid? OwnerUserId,
    LeadStatus? Status,
    string? Search,
    int Page = 1,
    int PageSize = 50);

public sealed record LeadCardDto(
    Guid Id,
    string ContactName,
    string? Phone,
    Guid OwnerUserId,
    string OwnerName,
    Guid StageId,
    LeadStatus Status,
    string? Source,
    string? Interest,
    int? Score,
    IReadOnlyList<string> Tags,
    DateTimeOffset EnteredAt,
    DateTimeOffset StageEnteredAt,
    DateTimeOffset? LastContactAt,
    DateTimeOffset? NextFollowUpAt,
    uint Version);

public sealed record LeadDetailDto(
    Guid Id,
    Guid ContactId,
    string FullName,
    string? Phone,
    string? Email,
    Guid OwnerUserId,
    string OwnerName,
    Guid PipelineId,
    Guid StageId,
    string StageName,
    LeadStatus Status,
    string? Source,
    string? Campaign,
    string? Interest,
    Guid? DevelopmentId,
    Guid? LotId,
    long? BudgetMinCents,
    long? BudgetMaxCents,
    PurchaseTimeframe Timeframe,
    string? Region,
    string? Notes,
    IReadOnlyList<string> Tags,
    int? Score,
    string? LostReason,
    DateTimeOffset EnteredAt,
    DateTimeOffset StageEnteredAt,
    DateTimeOffset? LastContactAt,
    DateTimeOffset? NextFollowUpAt,
    uint Version);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record BoardStageDto(Guid Id, string Name, string Kind, string? Color, int Position, int Total, IReadOnlyList<LeadCardDto> Leads);

public sealed record BoardDto(Guid PipelineId, string PipelineName, IReadOnlyList<BoardStageDto> Stages);

public sealed record LeadHistoryItemDto(DateTimeOffset OccurredAt, string Action, string ActorKind, Guid? ActorId, string? ActorName, string Data);
