using Loteadora.Crm.Modules.Crm.Domain;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Loteadora.Crm.Modules.Crm.Application;

/// <summary>Escopo de visibilidade de leads (own/team/org). O filtro de tenant já é global.</summary>
public static class LeadScope
{
    public static IQueryable<Lead> Apply(AppDbContext db, ICurrentActor actor, CapabilityScope scope)
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
}

public sealed record LeadRef(Guid Id, string ContactName, string? FirstName, string? Interest, Guid OwnerUserId);

/// <summary>
/// Contrato do módulo CRM para outros módulos (Agenda, Tarefas, Macros, futuramente Copilot).
/// Garante que qualquer referência a um lead respeite o escopo de leitura do ator.
/// </summary>
public sealed class LeadAccess(AppDbContext db, ICurrentActor actor)
{
    public async Task<LeadRef> GetVisibleAsync(Guid leadId, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.LeadsRead);
        var lead = await (
                from l in LeadScope.Apply(db, actor, scope)
                join c in db.Contacts on l.ContactId equals c.Id
                where l.Id == leadId
                select new { l.Id, c.FullName, l.Interest, l.OwnerUserId })
            .AsNoTracking()
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Lead");

        return new LeadRef(lead.Id, lead.FullName, lead.FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), lead.Interest, lead.OwnerUserId);
    }

    /// <summary>
    /// Atualiza o próximo follow-up do lead (sem salvar — o chamador salva na mesma transação).
    /// Não exige escopo de leitura: é uma derivação interna disparada por operações já autorizadas.
    /// </summary>
    public async Task SetNextFollowUpAsync(Guid leadId, DateTimeOffset? nextFollowUpAt, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        lead?.SetNextFollowUp(nextFollowUpAt);
    }

    public async Task RegisterContactAsync(Guid leadId, DateTimeOffset at, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        lead?.RegisterContact(at);
    }
}
