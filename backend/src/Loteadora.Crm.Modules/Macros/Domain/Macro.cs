using System.Text.RegularExpressions;
using Loteadora.Crm.SharedKernel.Domain;

namespace Loteadora.Crm.Modules.Macros.Domain;

/// <summary>
/// Resposta rápida (ex.: /apresentacao). OwnerUserId nulo = macro compartilhada da organização;
/// preenchido = macro pessoal do corretor. Anexos/mídia entram com a integração WhatsApp (Fase 3).
/// </summary>
public sealed partial class Macro : Entity, ITenantOwned, IHasTimestamps
{
    private Macro() { }

    public Macro(Guid organizationId, Guid? ownerUserId, string shortcut, string title, string body, Guid? developmentId)
    {
        OrganizationId = organizationId;
        OwnerUserId = ownerUserId;
        Update(shortcut, title, body, developmentId, isActive: true);
    }

    public Guid OrganizationId { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public string Shortcut { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public Guid? DevelopmentId { get; private set; }
    public bool IsActive { get; private set; }
    public int UsageCount { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsShared => OwnerUserId is null;

    public void Update(string shortcut, string title, string body, Guid? developmentId, bool isActive)
    {
        var normalized = "/" + (shortcut ?? "").Trim().TrimStart('/').ToLowerInvariant();
        if (!ShortcutPattern().IsMatch(normalized))
            throw new DomainException("macro.shortcut_invalid", "Atalho deve começar com / e ter de 2 a 32 caracteres (letras, números, - ou _).");
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("macro.title_required", "Informe o título.");
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("macro.body_required", "Informe o texto da macro.");

        var unknown = MacroTemplate.UnknownVariables(body);
        if (unknown.Count > 0)
            throw new DomainException("macro.unknown_variables", $"Variáveis desconhecidas: {string.Join(", ", unknown.Select(v => "{{" + v + "}}"))}.");

        Shortcut = normalized;
        Title = Guard.MaxLength(title.Trim(), 120, "Título")!;
        Body = Guard.MaxLength(body.Trim(), 4000, "Texto")!;
        DevelopmentId = developmentId;
        IsActive = isActive;
    }

    public void RegisterUse() => UsageCount++;

    [GeneratedRegex("^/[a-z0-9_-]{2,32}$")]
    private static partial Regex ShortcutPattern();
}

public sealed record RenderResult(string Text, IReadOnlyList<string> MissingVariables);

/// <summary>
/// Renderização de templates {{variavel}} com LISTA BRANCA de variáveis. Não há expressões,
/// laços ou acesso a objetos arbitrários — o texto é apenas substituído.
/// </summary>
public static partial class MacroTemplate
{
    public static readonly IReadOnlyDictionary<string, string> Variables = new Dictionary<string, string>
    {
        ["lead.first_name"] = "Primeiro nome do lead",
        ["lead.name"] = "Nome completo do lead",
        ["lead.interest"] = "Interesse do lead",
        ["agent.name"] = "Nome do corretor",
        ["agent.first_name"] = "Primeiro nome do corretor",
        ["org.name"] = "Nome da empresa",
        ["today"] = "Data de hoje",
    };

    public static IReadOnlyList<string> UnknownVariables(string body) =>
        VariablePattern().Matches(body).Select(m => m.Groups[1].Value).Where(v => !Variables.ContainsKey(v)).Distinct().ToList();

    public static RenderResult Render(string body, IReadOnlyDictionary<string, string?> values)
    {
        var missing = new List<string>();
        var text = VariablePattern().Replace(body, match =>
        {
            var name = match.Groups[1].Value;
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
            missing.Add(name);
            return "";
        });
        return new RenderResult(text, missing.Distinct().ToList());
    }

    [GeneratedRegex(@"\{\{\s*([a-z_]+(?:\.[a-z_]+)?)\s*\}\}")]
    private static partial Regex VariablePattern();
}
