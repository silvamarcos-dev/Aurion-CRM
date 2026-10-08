using Loteadora.Crm.SharedKernel.Domain;

namespace Loteadora.Crm.Modules.Crm.Domain;

/// <summary>
/// A pessoa (titular de dados). Dados pessoais ficam concentrados aqui para facilitar
/// atendimento a direitos do titular (acesso, correção, anonimização).
/// Um Contact pode ter vários Leads (interesses comerciais).
/// </summary>
public sealed class Contact : Entity, ITenantOwned, IHasTimestamps
{
    private Contact() { }

    public Contact(Guid organizationId, string fullName, string? phoneE164, string? email)
    {
        OrganizationId = organizationId;
        Update(fullName, phoneE164, email);
    }

    public Guid OrganizationId { get; private set; }
    public string FullName { get; private set; } = default!;
    public string? PhoneE164 { get; private set; }
    public string? Email { get; private set; }
    public DateTimeOffset? AnonymizedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public string FirstName => FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? FullName;

    public void Update(string fullName, string? phoneE164, string? email)
    {
        if (AnonymizedAt is not null)
            throw new DomainException("contact.anonymized", "Contato anonimizado não pode ser alterado.");
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("contact.name_required", "Nome é obrigatório.");

        Guard.MaxLength(fullName.Trim(), 200, "Nome");
        if (!string.IsNullOrWhiteSpace(email) && (email.Length > 254 || !email.Contains('@', StringComparison.Ordinal)))
            throw new DomainException("contact.email_invalid", "E-mail inválido.");

        FullName = fullName.Trim();
        PhoneE164 = string.IsNullOrWhiteSpace(phoneE164) ? null : PhoneNumber.NormalizeE164(phoneE164);
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }

    public void Anonymize(DateTimeOffset now)
    {
        FullName = "Titular anonimizado";
        PhoneE164 = null;
        Email = null;
        AnonymizedAt = now;
    }
}

public static class PhoneNumber
{
    /// <summary>
    /// Normaliza para E.164. Números brasileiros sem DDI recebem +55.
    /// (Validação completa com libphonenumber entra junto com a integração WhatsApp.)
    /// </summary>
    public static string NormalizeE164(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length is 10 or 11)
            digits = "55" + digits;
        if (digits.Length is < 8 or > 15)
            throw new DomainException("phone.invalid", "Telefone inválido.");
        return "+" + digits;
    }
}
