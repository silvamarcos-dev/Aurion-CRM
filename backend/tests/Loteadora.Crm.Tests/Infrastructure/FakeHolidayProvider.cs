using Loteadora.Crm.Modules.BusinessCalendar.Application;

namespace Loteadora.Crm.Tests.Infrastructure;

/// <summary>Provedor de feriados determinístico para testes (sem chamadas externas).</summary>
public sealed class FakeHolidayProvider : IHolidayProvider
{
    public string Name => "Fake";

    public Task<IReadOnlyList<ExternalHoliday>> GetNationalHolidaysAsync(int year, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExternalHoliday>>(
        [
            new ExternalHoliday(new DateOnly(year, 1, 1), "Confraternização Universal"),
            new ExternalHoliday(new DateOnly(year, 12, 25), "Natal"),
        ]);
}
