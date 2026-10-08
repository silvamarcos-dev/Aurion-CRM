using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.Modules.Pipeline.Domain;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Loteadora.Crm.Modules.Pipeline.Application;

public sealed record StageDto(Guid Id, string Name, string Kind, string? Color, int Position, bool IsArchived);

public sealed record PipelineDto(Guid Id, string Name, bool IsDefault, IReadOnlyList<StageDto> Stages);

public sealed record AddStageCommand(string Name, StageKind Kind, string? Color);

public sealed record UpdateStageCommand(string Name, string? Color);

public sealed record ReorderStagesCommand(IReadOnlyList<Guid> StageIds);

/// <summary>Configuração de pipelines/etapas (Kanban configurável). Etapas são arquivadas, nunca excluídas.</summary>
public sealed class PipelineService(AppDbContext db, ICurrentActor actor, IAuditTrail audit)
{
    public async Task<IReadOnlyList<PipelineDto>> ListAsync(CancellationToken ct)
    {
        actor.Require(Capabilities.LeadsRead);
        var pipelines = await db.Pipelines.Include(p => p.Stages).AsNoTracking().OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToListAsync(ct);
        return pipelines.Select(ToDto).ToList();
    }

    public async Task<StageDto> AddStageAsync(Guid pipelineId, AddStageCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.PipelineConfigure);
        ValidateName(cmd.Name);
        var pipeline = await LoadAsync(pipelineId, ct);
        var stage = pipeline.AddStage(cmd.Name.Trim(), cmd.Kind, cmd.Color);
        audit.Record(AuditActions.PipelineConfigured, "pipeline", pipeline.Id, new { change = "stage_added", stageId = stage.Id, cmd.Name, kind = cmd.Kind.ToString() });
        await db.SaveChangesAsync(ct);
        return ToDto(stage);
    }

    public async Task UpdateStageAsync(Guid pipelineId, Guid stageId, UpdateStageCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.PipelineConfigure);
        ValidateName(cmd.Name);
        var pipeline = await LoadAsync(pipelineId, ct);
        var stage = pipeline.Stages.FirstOrDefault(s => s.Id == stageId) ?? throw new NotFoundException("Etapa");
        stage.Rename(cmd.Name.Trim(), cmd.Color);
        audit.Record(AuditActions.PipelineConfigured, "pipeline", pipeline.Id, new { change = "stage_updated", stageId, cmd.Name });
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveStageAsync(Guid pipelineId, Guid stageId, CancellationToken ct)
    {
        actor.Require(Capabilities.PipelineConfigure);
        var pipeline = await LoadAsync(pipelineId, ct);
        var stage = pipeline.Stages.FirstOrDefault(s => s.Id == stageId) ?? throw new NotFoundException("Etapa");

        if (await db.Leads.AnyAsync(l => l.StageId == stageId, ct))
            throw new DomainException("stage.not_empty", "Mova os leads desta etapa antes de arquivá-la.");

        stage.Archive();
        audit.Record(AuditActions.PipelineConfigured, "pipeline", pipeline.Id, new { change = "stage_archived", stageId });
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(Guid pipelineId, ReorderStagesCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.PipelineConfigure);
        var pipeline = await LoadAsync(pipelineId, ct);
        pipeline.Reorder(cmd.StageIds);
        audit.Record(AuditActions.PipelineConfigured, "pipeline", pipeline.Id, new { change = "stages_reordered", order = cmd.StageIds });
        await db.SaveChangesAsync(ct);
    }

    private async Task<SalesPipeline> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Pipelines.Include(p => p.Stages).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Pipeline");

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
            throw new DomainException("stage.name_invalid", "Nome da etapa deve ter entre 1 e 80 caracteres.");
    }

    private static PipelineDto ToDto(SalesPipeline p) =>
        new(p.Id, p.Name, p.IsDefault, p.Stages.OrderBy(s => s.Position).Select(ToDto).ToList());

    private static StageDto ToDto(PipelineStage s) =>
        new(s.Id, s.Name, s.Kind.ToString(), s.Color, s.Position, s.IsArchived);
}
