using System.Net.Http.Json;

namespace Loteadora.Crm.Modules.BusinessCalendar.Application;

public sealed record ExternalHoliday(DateOnly Date, string Name);

/// <summary>Fonte externa de feriados. Implementações não devem receber dados pessoais.</summary>
public interface IHolidayProvider
{
    string Name { get; }

    Task<IReadOnlyList<ExternalHoliday>> GetNationalHolidaysAsync(int year, CancellationToken ct);
}

/// <summary>
/// BrasilAPI (https://brasilapi.com.br) — feriados nacionais.
/// Feriados estaduais e municipais são cadastrados manualmente até integrarmos uma fonte confiável.
/// </summary>
public sealed class BrasilApiHolidayProvider(HttpClient http) : IHolidayProvider
{
    private sealed record BrasilApiHoliday(string Date, string Name, string Type);

    public string Name => "BrasilAPI";

    public async Task<IReadOnlyList<ExternalHoliday>> GetNationalHolidaysAsync(int year, CancellationToken ct)
    {
        if (year is < 1900 or > 2199)
            throw new ArgumentOutOfRangeException(nameof(year));

        var items = await http.GetFromJsonAsync<List<BrasilApiHoliday>>($"api/feriados/v1/{year}", ct) ?? [];
        return items
            .Where(i => DateOnly.TryParse(i.Date, System.Globalization.CultureInfo.InvariantCulture, out _))
            .Select(i => new ExternalHoliday(DateOnly.Parse(i.Date, System.Globalization.CultureInfo.InvariantCulture), i.Name))
            .ToList();
    }
}
