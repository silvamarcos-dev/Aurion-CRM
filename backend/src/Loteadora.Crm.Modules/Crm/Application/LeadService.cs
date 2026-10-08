using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.Crm.Domain;
using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.Modules.Pipeline.Domain;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Loteadora.Crm.Modules.Crm.Application;

/// <summary>
/// Casos de uso de Leads. Toda operação:
///  - exige capability (RBAC no backend);
///  - aplica o ESCOPO da capability (own/team/org) na própria consulta — recurso fora do escopo = 404 (anti-IDOR);
///  - audita na mesma transação.
/// O mesmo serviço será usado pelo Copilot (via Tool Gateway), sob as mesmas regras.
/// </summary>
public sealed class LeadService(AppDbContext db, ICurrentActor actor, IAuditTrail audit, TimeProvider clock)
{
    private const int BoardStageLimit = 100;

    public async Task<PagedResult<LeadCardDto>> ListAsync(LeadListQuery query, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.LeadsRead);
        var leads = Scoped(scope);

        if (query.PipelineId is { } pipelineId) leads = leads.Where(l => l.PipelineId == pipelineId);
        if (query.StageId is { } stageId) leads = leads.Where(l => l.StageId == stageId);
        if (query.OwnerUserId is { } ownerId) leads = leads.Where(l => l.OwnerUserId == ownerId);
        if (query.Status is { } status) leads = leads.Where(l => l.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            var contactIds = db.Contacts
                .Where(c => EF.Functions.ILike(c.FullName, term) || (c.Email != null && EF.Functions.ILike(c.Email, term)) || (c.PhoneE164 != null && EF.Functions.ILike(c.PhoneE164, term)))
                .Select(c => c.Id);
            leads = leads.Where(l => contactIds.Contains(l.ContactId));
        }

        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var page = Math.Max(1, query.Page);
        var total = await leads.CountAsync(ct);
        var items = await ProjectCards(leads.OrderByDescending(l => l.EnteredAt).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return new PagedResult<LeadCardDto>(items, page, pageSize, total);
    }

    public async Task<LeadDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.LeadsRead);
        var detail = await (
                from l in Scoped(scope)
                join c in db.Contacts on l.ContactId equals c.Id
                join u in db.Users on l.OwnerUserId equals u.Id
                join s in db.PipelineStages on l.StageId equals s.Id
                where l.Id == id
                select new LeadDetailDto(
                    l.Id, c.Id, c.FullName, c.PhoneE164, c.Email, l.OwnerUserId, u.DisplayName,
                    l.PipelineId, l.StageId, s.Name, l.Status, l.Source, l.Campaign, l.Interest,
                    l.DevelopmentId, l.LotId, l.BudgetMinCents, l.BudgetMaxCents, l.Timeframe,
                    l.Region, l.Notes, l.Tags, l.Score, l.LostReason, l.EnteredAt, l.StageEnteredAt,
                    l.LastContactAt, l.NextFollowUpAt, l.Version))
            .AsNoTracking()
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Lead");

        // Registro de acesso a dado pessoal (LGPD — auditoria de acesso).
        audit.Record(AuditActions.LeadViewed, "lead", id);
        await db.SaveChangesAsync(ct);
        return detail;
    }

    public async Task<Guid> CreateAsync(CreateLeadCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.LeadsCreate);
        var now = clock.GetUtcNow();

        var ownerId = cmd.OwnerUserId ?? actor.UserId;
        if (ownerId != actor.UserId || actor.Kind != ActorKind.User)
            await EnsureCanAssignToAsync(ownerId, ct);

        var stage = cmd.StageId is { } stageId
            ? await db.PipelineStages.FirstOrDefaultAsync(s => s.Id == stageId && !s.IsArchived, ct) ?? throw new NotFoundException("Etapa")
            : await DefaultEntryStageAsync(ct);

        // Deduplicação básica por telefone/e-mail dentro do tenant: reutiliza o titular.
        var phone = string.IsNullOrWhiteSpace(cmd.Phone) ? null : PhoneNumber.NormalizeE164(cmd.Phone);
        var email = string.IsNullOrWhiteSpace(cmd.Email) ? null : cmd.Email.Trim().ToLowerInvariant();
        var contact = await db.Contacts.FirstOrDefaultAsync(c =>
            c.AnonymizedAt == null && ((phone != null && c.PhoneE164 == phone) || (email != null && c.Email == email)), ct);
        if (contact is null)
        {
            contact = new Contact(actor.OrganizationId, cmd.FullName, phone, email);
            db.Contacts.Add(contact);
        }

        var lead = new Lead(actor.OrganizationId, contact.Id, ownerId, stage, cmd.Source, cmd.Campaign, now);
        lead.UpdateDetails(cmd.Interest, null, null, null, null, PurchaseTimeframe.Unknown, null, null, null, null);
        db.Leads.Add(lead);

        audit.Record(AuditActions.LeadCreated, "lead", lead.Id, new { ownerUserId = ownerId, stageId = stage.Id, cmd.Source, cmd.Campaign, contactReused = db.Entry(contact).State != EntityState.Added });
        await db.SaveChangesAsync(ct);
        return lead.Id;
    }

    public async Task<uint> UpdateAsync(Guid id, UpdateLeadCommand cmd, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.LeadsUpdate);
        var lead = await LoadTrackedAsync(id, scope, cmd.ExpectedVersion, ct);
        var contact = await db.Contacts.FirstAsync(c => c.Id == lead.ContactId, ct);

        contact.Update(cmd.FullName, cmd.Phone, cmd.Email);
        lead.UpdateDetails(cmd.Interest, cmd.DevelopmentId, cmd.LotId, cmd.BudgetMinCents, cmd.BudgetMaxCents,
            cmd.Timeframe, cmd.Region, cmd.Notes, cmd.Tags, cmd.NextFollowUpAt);

        // Auditoria registra QUAIS campos mudaram — sem valores de dados pessoais.
        var changed = db.Entry(lead).Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name)
            .Concat(db.Entry(contact).Properties.Where(p => p.IsModified).Select(p => "contact." + p.Metadata.Name))
            .Where(n => n is not nameof(Lead.UpdatedAt) and not "contact.UpdatedAt")
            .ToList();
        if (changed.Count == 0)
            return lead.Version;

        audit.Record(AuditActions.LeadUpdated, "lead", lead.Id, new { fields = changed });
        await SaveWithConcurrencyAsync(ct);
        return lead.Version;
    }

    public async Task<uint> MoveAsync(Guid id, MoveLeadCommand cmd, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.PipelineMove);
        var lead = await LoadTrackedAsync(id, scope, cmd.ExpectedVersion, ct);
        var target = await db.PipelineStages.FirstOrDefaultAsync(s => s.Id == cmd.StageId, ct)
                     ?? throw new NotFoundException("Etapa");

        var fromStageId = lead.StageId;
        var transition = lead.MoveTo(target, actor.Kind, ActorIdOrNull(), cmd.Reason, clock.GetUtcNow());
        db.LeadStageTransitions.Add(transition);

        audit.Record(AuditActions.LeadStageChanged, "lead", lead.Id, new { fromStageId, toStageId = target.Id, toStageKind = target.Kind.ToString(), reason = cmd.Reason });
        await SaveWithConcurrencyAsync(ct);
        return lead.Version;
    }

    public async Task AssignAsync(Guid id, AssignLeadCommand cmd, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.LeadsAssign);
        var lead = await Scoped(scope).FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Lead");
        await EnsureCanAssignToAsync(cmd.OwnerUserId, ct);

        var previous = lead.OwnerUserId;
        lead.AssignTo(cmd.OwnerUserId, actor.Kind, ActorIdOrNull());
        audit.Record(AuditActions.LeadAssigned, "lead", lead.Id, new { previousOwnerUserId = previous, newOwnerUserId = cmd.OwnerUserId });
        await SaveWithConcurrencyAsync(ct);
    }

    public async Task<BoardDto> GetBoardAsync(Guid? pipelineId, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.LeadsRead);
        var pipeline = await db.Pipelines.Include(p => p.Stages)
                           .Where(p => pipelineId == null ? p.IsDefault : p.Id == pipelineId)
                           .AsNoTracking()
                           .FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Pipeline");

        var stages = pipeline.Stages.Where(s => !s.IsArchived).OrderBy(s => s.Position).ToList();
        var scoped = Scoped(scope).Where(l => l.PipelineId == pipeline.Id && l.Status != LeadStatus.Archived);

        var totals = await scoped.GroupBy(l => l.StageId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var result = new List<BoardStageDto>(stages.Count);
        foreach (var stage in stages)
        {
            var cards = await ProjectCards(scoped.Where(l => l.StageId == stage.Id).OrderByDescending(l => l.StageEnteredAt).Take(BoardStageLimit)).ToListAsync(ct);
            result.Add(new BoardStageDto(stage.Id, stage.Name, stage.Kind.ToString(), stage.Color, stage.Position, totals.GetValueOrDefault(stage.Id), cards));
        }

        return new BoardDto(pipeline.Id, pipeline.Name, result);
    }

    public async Task<IReadOnlyList<LeadHistoryItemDto>> GetHistoryAsync(Guid id, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.LeadsRead);
        if (!await Scoped(scope).AnyAsync(l => l.Id == id, ct))
            throw new NotFoundException("Lead");

        return await (
                from a in db.AuditEvents
                where a.EntityType == "lead" && a.EntityId == id && a.Action != AuditActions.LeadViewed
                join u in db.Users on a.ActorId equals u.Id into users
                from u in users.DefaultIfEmpty()
                orderby a.Sequence descending
                select new LeadHistoryItemDto(a.OccurredAt, a.Action, a.ActorKind.ToString(), a.ActorId, u != null ? u.DisplayName : null, a.Data))
            .Take(200)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    // ───────────────────────── helpers ─────────────────────────

    /// <summary>Aplica o escopo da capability na consulta (o filtro de tenant já é global).</summary>
    private IQueryable<Lead> Scoped(CapabilityScope scope)
    {
        var leads = db.Leads.AsQueryable();
        switch (scope)
        {
            case CapabilityScope.Org:
                return leads;
            case CapabilityScope.Team when actor.TeamId is { } teamId:
                var teamUserIds = db.Memberships.Where(m => m.TeamId == teamId).Select(m => m.UserId);
                return leads.Where(l => teamUserIds.Contains(l.OwnerUserId));
            default:
                var userId = actor.UserId;
                return leads.Where(l => l.OwnerUserId == userId);
        }
    }

    private async Task<Lead> LoadTrackedAsync(Guid id, CapabilityScope scope, uint expectedVersion, CancellationToken ct)
    {
        var lead = await Scoped(scope).FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Lead");
        if (lead.Version != expectedVersion)
            throw new ConcurrencyException("O lead foi alterado por outra pessoa. Recarregue e tente novamente.");
        return lead;
    }

    private async Task SaveWithConcurrencyAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("O lead foi alterado por outra pessoa. Recarregue e tente novamente.");
        }
    }

    private async Task EnsureCanAssignToAsync(Guid targetUserId, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.LeadsAssign);
        var target = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == targetUserId && m.Status == MembershipStatus.Active, ct)
                     ?? throw new NotFoundException("Corretor");

        var allowed = scope switch
        {
            CapabilityScope.Org => true,
            CapabilityScope.Team => actor.TeamId is not null && target.TeamId == actor.TeamId,
            _ => targetUserId == actor.UserId,
        };
        if (!allowed)
            throw new ForbiddenException(Capabilities.LeadsAssign);
    }

    private async Task<PipelineStage> DefaultEntryStageAsync(CancellationToken ct) =>
        await (from p in db.Pipelines
               join s in db.PipelineStages on p.Id equals s.PipelineId
               where p.IsDefault && !s.IsArchived && s.Kind == StageKind.Open
               orderby s.Position
               select s).FirstOrDefaultAsync(ct)
        ?? throw new DomainException("pipeline.missing_default", "Nenhum pipeline padrão configurado.");

    private IQueryable<LeadCardDto> ProjectCards(IQueryable<Lead> leads) =>
        from l in leads
        join c in db.Contacts on l.ContactId equals c.Id
        join u in db.Users on l.OwnerUserId equals u.Id
        select new LeadCardDto(l.Id, c.FullName, c.PhoneE164, l.OwnerUserId, u.DisplayName, l.StageId, l.Status,
            l.Source, l.Interest, l.Score, l.Tags, l.EnteredAt, l.StageEnteredAt, l.LastContactAt, l.NextFollowUpAt, l.Version);

    private Guid? ActorIdOrNull() => actor.Kind == ActorKind.System ? null : actor.UserId;
}
