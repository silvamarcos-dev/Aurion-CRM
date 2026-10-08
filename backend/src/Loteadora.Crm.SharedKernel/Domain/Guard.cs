namespace Loteadora.Crm.SharedKernel.Domain;

public static class Guard
{
    public static string? MaxLength(string? value, int max, string field)
    {
        if (value is not null && value.Length > max)
            throw new DomainException("validation.max_length", $"{field} deve ter no máximo {max} caracteres.");
        return value;
    }
}
