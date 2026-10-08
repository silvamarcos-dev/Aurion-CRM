using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.Modules.Suggestions.Domain;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Loteadora.Crm.Modules.Suggestions.Application;

public sealed record CreateSuggestionCommand(SuggestionKind Kind, string Title, string Description, string PagePath, string? PageTitle, string? ElementDescriptor, string? Viewport);

public sealed record TriageSuggestionCommand(SuggestionStatus Status, SuggestionPriority? Priority);

public sealed record AddCommentCommand(string Body);

public sealed record SuggestionSummaryDto(Guid Id, SuggestionKind Kind, string Title, string PagePath, SuggestionStatus Status, SuggestionPriority? Priority,
    Guid AuthorUserId, string AuthorName, string AuthorRole, int CommentCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record SuggestionCommentDto(Guid Id, Guid AuthorUserId, string AuthorName, string Body, DateTimeOffset CreatedAt);

public sealed record SuggestionDetailDto(Guid Id, SuggestionKind Kind, string Title, string Description, string PagePath, string? PageTitle, string? ElementDescriptor,
    string? Viewport, SuggestionStatus Status, SuggestionPriority? Priority, Guid AuthorUserId, string AuthorName, string AuthorRole,
    DateTimeOffset CreatedAt, IReadOnlyList<SuggestionCommentDto> Comments, bool CanTriage, bool CanComment);

public sealed record SuggestionStatsDto(int New, int UnderReview, int Planned, int Done, int Declined);

/// <summary>
/// "Sugerir mudança": qualquer usuário com suggestions.create envia; suggestions.read (Own) vê as próprias,
/// (Org) vê todas; suggestions.manage faz a triagem. O autor e quem gerencia podem comentar.
/// </summary>
public sealed class SuggestionService(AppDbContext db, ICurrentActor actor, IAuditTrail audit)
{
    public async Task<Guid> CreateAsync(CreateSuggestionCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.SuggestionsCreate);
        var suggestion = new ChangeSuggestion(actor.OrganizationId, actor.UserId, cmd.Kind, cmd.Title, cmd.Description, cmd.PagePath, cmd.PageTitle, cmd.ElementDescriptor, cmd.Viewport);
        db.Suggestions.Add(suggestion);
        audit.Record("suggestion.created", "suggestion", suggestion.Id, new { kind = cmd.Kind.ToString(), suggestion.PagePath });
        await db.SaveChangesAsync(ct);
        return suggestion.Id;
    }

    public async Task<IReadOnlyList<SuggestionSummaryDto>> ListAsync(SuggestionStatus? status, SuggestionKind? kind, CancellationToken ct)
    {
        var query = Visible();
        if (status is { } s) query = query.Where(x => x.Status == s);
        if (kind is { } k) query = query.Where(x => x.Kind == k);

        return await (
                from x in query
                join u in db.Users on x.AuthorUserId equals u.Id
                join m in db.Memberships on x.AuthorUserId equals m.UserId
                join r in db.Roles on m.RoleId equals r.Id
                orderby x.CreatedAt descending
                select new SuggestionSummaryDto(x.Id, x.Kind, x.Title, x.PagePath, x.Status, x.Priority, x.AuthorUserId, u.DisplayName, r.Name,
                    db.SuggestionComments.Count(c => c.SuggestionId == x.Id), x.CreatedAt, x.UpdatedAt))
            .Take(300)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<SuggestionStatsDto> GetStatsAsync(CancellationToken ct)
    {
        var counts = await Visible().GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return new SuggestionStatsDto(counts.GetValueOrDefault(SuggestionStatus.New), counts.GetValueOrDefault(SuggestionStatus.UnderReview),
            counts.GetValueOrDefault(SuggestionStatus.Planned), counts.GetValueOrDefault(SuggestionStatus.Done), counts.GetValueOrDefault(SuggestionStatus.Declined));
    }

    public async Task<SuggestionDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await (
                from x in Visible()
                join u in db.Users on x.AuthorUserId equals u.Id
                join m in db.Memberships on x.AuthorUserId equals m.UserId
                join r in db.Roles on m.RoleId equals r.Id
                where x.Id == id
                select new { x, AuthorName = u.DisplayName, Role = r.Name })
            .AsNoTracking()
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Sugestão");

        var comments = await (
                from c in db.SuggestionComments
                join u in db.Users on c.AuthorUserId equals u.Id
                where c.SuggestionId == id
                orderby c.CreatedAt
                select new SuggestionCommentDto(c.Id, c.AuthorUserId, u.DisplayName, c.Body, c.CreatedAt))
            .AsNoTracking()
            .ToListAsync(ct);

        var s = row.x;
        var canTriage = actor.Has(Capabilities.SuggestionsManage);
        return new SuggestionDetailDto(s.Id, s.Kind, s.Title, s.Description, s.PagePath, s.PageTitle, s.ElementDescriptor, s.Viewport, s.Status, s.Priority,
            s.AuthorUserId, row.AuthorName, row.Role, s.CreatedAt, comments, canTriage, canTriage || s.AuthorUserId == actor.UserId);
    }

    public async Task TriageAsync(Guid id, TriageSuggestionCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.SuggestionsManage);
        var suggestion = await db.Suggestions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Sugestão");
        var previous = suggestion.Status;
        suggestion.Triage(cmd.Status, cmd.Priority);
        audit.Record("suggestion.triaged", "suggestion", id, new { from = previous.ToString(), to = cmd.Status.ToString(), priority = cmd.Priority?.ToString() });
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> CommentAsync(Guid id, AddCommentCommand cmd, CancellationToken ct)
    {
        var suggestion = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Sugestão");
        if (suggestion.AuthorUserId != actor.UserId)
            actor.Require(Capabilities.SuggestionsManage);
        else
            actor.Require(Capabilities.SuggestionsCreate);

        var comment = new SuggestionComment(actor.OrganizationId, id, actor.UserId, cmd.Body);
        db.SuggestionComments.Add(comment);
        audit.Record("suggestion.commented", "suggestion", id, new { commentId = comment.Id });
        await db.SaveChangesAsync(ct);
        return comment.Id;
    }

    /// <summary>Escopo de leitura: Org vê todas; Own (ou sem capability de leitura, mas autor) vê só as próprias.</summary>
    private IQueryable<ChangeSuggestion> Visible()
    {
        var scope = actor.ScopeOf(Capabilities.SuggestionsRead) ?? actor.Require(Capabilities.SuggestionsCreate);
        var userId = actor.UserId;
        return scope == CapabilityScope.Org ? db.Suggestions : db.Suggestions.Where(x => x.AuthorUserId == userId);
    }
}
