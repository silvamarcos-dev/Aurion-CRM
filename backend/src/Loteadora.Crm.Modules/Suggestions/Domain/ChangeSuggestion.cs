using Loteadora.Crm.SharedKernel.Domain;

namespace Loteadora.Crm.Modules.Suggestions.Domain;

public enum SuggestionKind
{
    Bug = 1,
    Improvement = 2,
    Idea = 3,
    Text = 4,
}

public enum SuggestionStatus
{
    New = 1,
    UnderReview = 2,
    Planned = 3,
    Done = 4,
    Declined = 5,
}

public enum SuggestionPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>
/// Sugestão de mudança enviada de dentro do sistema (por usuários, visitantes ou desenvolvedores).
/// Guarda o CONTEXTO técnico (página, elemento apontado, tamanho da tela) — nunca dados de leads.
/// </summary>
public sealed class ChangeSuggestion : Entity, ITenantOwned, IHasTimestamps
{
    private ChangeSuggestion() { }

    public ChangeSuggestion(Guid organizationId, Guid authorUserId, SuggestionKind kind, string title, string description,
        string pagePath, string? pageTitle, string? elementDescriptor, string? viewport)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("suggestion.title_required", "Dê um título para a sugestão.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("suggestion.description_required", "Descreva a sugestão.");
        if (string.IsNullOrWhiteSpace(pagePath) || !pagePath.StartsWith('/'))
            throw new DomainException("suggestion.page_invalid", "Página inválida.");

        OrganizationId = organizationId;
        AuthorUserId = authorUserId;
        Kind = kind;
        Title = Guard.MaxLength(title.Trim(), 140, "Título")!;
        Description = Guard.MaxLength(description.Trim(), 4000, "Descrição")!;
        PagePath = Guard.MaxLength(SanitizePath(pagePath), 300, "Página")!;
        PageTitle = Guard.MaxLength(pageTitle?.Trim(), 140, "Título da página");
        ElementDescriptor = Guard.MaxLength(elementDescriptor?.Trim(), 600, "Elemento");
        Viewport = Guard.MaxLength(viewport?.Trim(), 20, "Tela");
    }

    public Guid OrganizationId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public SuggestionKind Kind { get; private set; }
    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string PagePath { get; private set; } = default!;
    public string? PageTitle { get; private set; }
    public string? ElementDescriptor { get; private set; }
    public string? Viewport { get; private set; }
    public SuggestionStatus Status { get; private set; } = SuggestionStatus.New;
    public SuggestionPriority? Priority { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Triage(SuggestionStatus status, SuggestionPriority? priority)
    {
        Status = status;
        Priority = priority;
    }

    /// <summary>Remove query string e fragmento (podem carregar identificadores/filtros) — fica só o caminho.</summary>
    private static string SanitizePath(string path)
    {
        var cut = path.IndexOfAny(['?', '#']);
        return (cut >= 0 ? path[..cut] : path).Trim();
    }
}

public sealed class SuggestionComment : Entity, ITenantOwned, IHasTimestamps
{
    private SuggestionComment() { }

    public SuggestionComment(Guid organizationId, Guid suggestionId, Guid authorUserId, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("suggestion.comment_required", "Escreva o comentário.");
        OrganizationId = organizationId;
        SuggestionId = suggestionId;
        AuthorUserId = authorUserId;
        Body = Guard.MaxLength(body.Trim(), 2000, "Comentário")!;
    }

    public Guid OrganizationId { get; private set; }
    public Guid SuggestionId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
