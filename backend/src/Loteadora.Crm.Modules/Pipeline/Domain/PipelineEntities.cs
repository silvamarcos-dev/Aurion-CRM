using Loteadora.Crm.SharedKernel.Domain;

namespace Loteadora.Crm.Modules.Pipeline.Domain;

public enum StageKind
{
    Open = 1,
    Won = 2,
    Lost = 3,
}

public sealed class SalesPipeline : Entity, ITenantOwned, IHasTimestamps
{
    private readonly List<PipelineStage> _stages = [];

    private SalesPipeline() { }

    public SalesPipeline(Guid organizationId, string name, bool isDefault)
    {
        OrganizationId = organizationId;
        Name = name;
        IsDefault = isDefault;
    }

    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = default!;
    public bool IsDefault { get; private set; }
    public IReadOnlyList<PipelineStage> Stages => _stages;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public PipelineStage AddStage(string name, StageKind kind, string? color = null)
    {
        var position = _stages.Count == 0 ? 0 : _stages.Max(s => s.Position) + 1;
        var stage = new PipelineStage(OrganizationId, Id, name, position, kind, color);
        _stages.Add(stage);
        return stage;
    }

    public void Reorder(IReadOnlyList<Guid> orderedStageIds)
    {
        var active = _stages.Where(s => !s.IsArchived).ToList();
        if (orderedStageIds.Count != active.Count || active.Any(s => !orderedStageIds.Contains(s.Id)))
            throw new DomainException("pipeline.reorder_mismatch", "A nova ordem deve conter exatamente as etapas ativas do pipeline.");

        for (var i = 0; i < orderedStageIds.Count; i++)
            active.Single(s => s.Id == orderedStageIds[i]).MoveTo(i);
    }
}

public sealed class PipelineStage : Entity, ITenantOwned
{
    private PipelineStage() { }

    internal PipelineStage(Guid organizationId, Guid pipelineId, string name, int position, StageKind kind, string? color)
    {
        OrganizationId = organizationId;
        PipelineId = pipelineId;
        Name = name;
        Position = position;
        Kind = kind;
        Color = color;
    }

    public Guid OrganizationId { get; private set; }
    public Guid PipelineId { get; private set; }
    public string Name { get; private set; } = default!;
    public int Position { get; private set; }
    public StageKind Kind { get; private set; }
    public string? Color { get; private set; }

    /// <summary>Etapas não são excluídas (preservam histórico); são arquivadas.</summary>
    public bool IsArchived { get; private set; }

    public void Rename(string name, string? color)
    {
        Name = name;
        Color = color;
    }

    public void Archive() => IsArchived = true;

    internal void MoveTo(int position) => Position = position;
}
