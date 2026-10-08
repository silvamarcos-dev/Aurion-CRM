using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.Crm.Application;
using Loteadora.Crm.Modules.Macros.Domain;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Loteadora.Crm.Modules.Macros.Application;

public sealed record MacroDto(Guid Id, string Shortcut, string Title, string Body, bool IsShared, Guid? OwnerUserId, Guid? DevelopmentId, bool IsActive, int UsageCount, bool CanEdit);

public sealed record SaveMacroCommand(string Shortcut, string Title, string Body, bool Shared, Guid? DevelopmentId, bool IsActive = true);

public sealed record RenderMacroCommand(Guid? LeadId);

public sealed record MacroVariableDto(string Name, string Description);

/// <summary>
/// Macros: o corretor usa as compartilhadas + as pessoais. Criar/editar compartilhadas exige macros.manage com escopo Org;
/// pessoais exigem macros.manage (qualquer escopo) e só o dono altera.
/// </summary>
public sealed class MacroService(AppDbContext db, ICurrentActor actor, IAuditTrail audit, LeadAccess leads, PiiMask mask,
    BusinessCalendar.Application.BusinessCalendarService calendars, TimeProvider clock)
{
    public static IReadOnlyList<MacroVariableDto> AvailableVariables =>
        MacroTemplate.Variables.Select(kv => new MacroVariableDto(kv.Key, kv.Value)).ToList();

    public async Task<IReadOnlyList<MacroDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        actor.Require(Capabilities.MacrosUse);
        var userId = actor.UserId;
        var macros = await db.Macros.AsNoTracking()
            .Where(m => (m.OwnerUserId == null || m.OwnerUserId == userId) && (includeInactive || m.IsActive))
            .OrderBy(m => m.Shortcut)
            .ToListAsync(ct);
        return macros.Select(ToDto).ToList();
    }

    public async Task<Guid> CreateAsync(SaveMacroCommand cmd, CancellationToken ct)
    {
        EnsureCanManage(cmd.Shared);
        var macro = new Macro(actor.OrganizationId, cmd.Shared ? null : actor.UserId, cmd.Shortcut, cmd.Title, cmd.Body, cmd.DevelopmentId);
        await EnsureShortcutAvailableAsync(macro.Shortcut, macro.OwnerUserId, null, ct);

        db.Macros.Add(macro);
        audit.Record("macro.created", "macro", macro.Id, new { macro.Shortcut, shared = cmd.Shared });
        await db.SaveChangesAsync(ct);
        return macro.Id;
    }

    public async Task UpdateAsync(Guid id, SaveMacroCommand cmd, CancellationToken ct)
    {
        var macro = await LoadEditableAsync(id, ct);
        if (cmd.Shared != macro.IsShared)
            throw new DomainException("macro.scope_change", "Não é possível converter macro pessoal em compartilhada (ou vice-versa). Crie uma nova.");

        macro.Update(cmd.Shortcut, cmd.Title, cmd.Body, cmd.DevelopmentId, cmd.IsActive);
        await EnsureShortcutAvailableAsync(macro.Shortcut, macro.OwnerUserId, macro.Id, ct);
        audit.Record("macro.updated", "macro", macro.Id, new { macro.Shortcut, macro.IsActive });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var macro = await LoadEditableAsync(id, ct);
        db.Macros.Remove(macro);
        audit.Record("macro.deleted", "macro", macro.Id, new { macro.Shortcut });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Renderiza a macro para um lead (que precisa estar no escopo do ator). Não envia nada.</summary>
    public async Task<RenderResult> RenderAsync(Guid id, RenderMacroCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.MacrosUse);
        var userId = actor.UserId;
        var macro = await db.Macros.FirstOrDefaultAsync(m => m.Id == id && (m.OwnerUserId == null || m.OwnerUserId == userId), ct)
                    ?? throw new NotFoundException("Macro");

        var lead = cmd.LeadId is { } leadId ? await leads.GetVisibleAsync(leadId, ct) : null;
        var agent = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        var now = clock.GetUtcNow();
        var calendar = await calendars.LoadAsync(DateOnly.FromDateTime(now.UtcDateTime), DateOnly.FromDateTime(now.UtcDateTime), ct);
        var today = NodaTime.Instant.FromDateTimeOffset(now).InZone(calendar.Zone).Date;
        var org = await db.Organizations.AsNoTracking().Where(o => o.Id == actor.OrganizationId).Select(o => o.Name).FirstOrDefaultAsync(ct);

        var values = new Dictionary<string, string?>
        {
            ["lead.first_name"] = lead?.FirstName,
            ["lead.name"] = lead is null ? null : mask.Name(lead.ContactName),
            ["lead.interest"] = lead?.Interest,
            ["agent.name"] = agent,
            ["agent.first_name"] = agent?.Split(' ', 2)[0],
            ["org.name"] = org,
            ["today"] = today.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
        };

        // Acessos externos (visitante/desenvolvedor) são somente leitura: não contabilizam uso.
        if (!mask.IsMasking)
        {
            macro.RegisterUse();
            await db.SaveChangesAsync(ct);
        }
        return MacroTemplate.Render(macro.Body, values);
    }

    private void EnsureCanManage(bool shared)
    {
        var scope = actor.Require(Capabilities.MacrosManage);
        if (shared && scope != CapabilityScope.Org)
            throw new ForbiddenException(Capabilities.MacrosManage + " (org)");
    }

    private async Task<Macro> LoadEditableAsync(Guid id, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.MacrosManage);
        var userId = actor.UserId;
        var macro = await db.Macros.FirstOrDefaultAsync(m => m.Id == id && (m.OwnerUserId == null || m.OwnerUserId == userId), ct)
                    ?? throw new NotFoundException("Macro");
        if (macro.IsShared && scope != CapabilityScope.Org)
            throw new ForbiddenException(Capabilities.MacrosManage + " (org)");
        return macro;
    }

    private async Task EnsureShortcutAvailableAsync(string shortcut, Guid? ownerUserId, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Macros.AnyAsync(m => m.Shortcut == shortcut && m.OwnerUserId == ownerUserId && m.Id != exceptId, ct))
            throw new DomainException("macro.shortcut_taken", $"O atalho {shortcut} já está em uso.");
    }

    private MacroDto ToDto(Macro m)
    {
        var scope = actor.ScopeOf(Capabilities.MacrosManage);
        var canEdit = scope is not null && (!m.IsShared || scope == CapabilityScope.Org);
        return new MacroDto(m.Id, m.Shortcut, m.Title, m.Body, m.IsShared, m.OwnerUserId, m.DevelopmentId, m.IsActive, m.UsageCount, canEdit);
    }
}
