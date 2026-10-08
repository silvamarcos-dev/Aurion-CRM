using Loteadora.Crm.SharedKernel.Security;

namespace Loteadora.Crm.Modules.Crm.Application;

/// <summary>
/// Mascaramento de dados pessoais de titulares (leads) para quem não tem a capability pii.view
/// — ex.: visitantes e desenvolvedores convidados. Aplicado NA API, nunca só na interface.
///   "Mariana Souza"         → "Mariana S."
///   "+5511987650002"        → "+55 11 •••••-0002"
///   "mariana@exemplo.com"   → "m•••@exemplo.com"
/// </summary>
public sealed class PiiMask(ICurrentActor actor)
{
    public bool IsMasking => !actor.Has(Capabilities.PiiView);

    public string Name(string fullName)
    {
        if (!IsMasking)
            return fullName;
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "—",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }

    public string? OptionalName(string? fullName) => fullName is null ? null : Name(fullName);

    public string? Phone(string? e164)
    {
        if (!IsMasking || string.IsNullOrEmpty(e164))
            return e164;
        var digits = new string(e164.Where(char.IsDigit).ToArray());
        if (digits.Length < 8)
            return "•••";
        var last4 = digits[^4..];
        return digits.StartsWith("55", StringComparison.Ordinal) && digits.Length >= 12
            ? $"+55 {digits.Substring(2, 2)} •••••-{last4}"
            : $"•••••-{last4}";
    }

    public string? Email(string? email)
    {
        if (!IsMasking || string.IsNullOrEmpty(email))
            return email;
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "•••" : $"{email[0]}•••{email[at..]}";
    }

    /// <summary>Remove campos de texto livre de um JSON de auditoria (motivos/observações podem conter dados pessoais).</summary>
    public string AuditData(string json)
    {
        if (!IsMasking)
            return json;
        if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject obj)
            return "{}";
        foreach (var key in new[] { "reason", "notes", "note" })
            obj.Remove(key);
        return obj.ToJsonString();
    }

    /// <summary>Texto livre (observações) pode conter dados pessoais: ocultado por completo.</summary>
    public string? FreeText(string? text) => IsMasking && !string.IsNullOrEmpty(text) ? "[oculto no modo visitante]" : text;
}
