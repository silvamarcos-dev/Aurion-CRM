using Loteadora.Crm.Modules.BusinessCalendar.Domain;
using NodaTime;
using NodaTime.Text;
using CalendarModel = Loteadora.Crm.Modules.BusinessCalendar.Domain.BusinessCalendar;

namespace Loteadora.Crm.Tests.Unit;

public sealed class BusinessCalendarTests
{
    private static readonly Guid Org = Guid.CreateVersion7();

    private static CalendarModel Calendar(IEnumerable<Holiday>? holidays = null, IEnumerable<CalendarException>? exceptions = null, Action<CalendarSettings>? configure = null)
    {
        var settings = CalendarSettings.CreateDefault(Org);
        configure?.Invoke(settings);
        return new CalendarModel(settings, holidays ?? [], exceptions ?? []);
    }

    /// <summary>Horário local de São Paulo (UTC-3) → Instant.</summary>
    private static Instant Sp(string localDateTime) =>
        LocalDateTimePattern.GeneralIso.Parse(localDateTime).Value.InZoneStrictly(DateTimeZoneProviders.Tzdb["America/Sao_Paulo"]).ToInstant();

    private static Holiday ConfirmedHoliday(DateOnly date, string name, HolidayScope scope = HolidayScope.National, string? uf = null, string? ibge = null)
    {
        var h = new Holiday(Org, date, name, scope, uf, ibge, HolidaySource.Api);
        h.Confirm();
        return h;
    }

    [Theory]
    [InlineData("2026-10-08T07:59:59", false)]
    [InlineData("2026-10-08T08:00:00", true)]
    [InlineData("2026-10-08T17:59:59", true)]
    [InlineData("2026-10-08T18:00:00", false)] // intervalo semiaberto: 18:00 já é fora do expediente
    [InlineData("2026-10-08T18:01:00", false)]
    [InlineData("2026-10-10T10:00:00", false)] // sábado
    public void Business_time_respects_half_open_intervals(string local, bool expected) =>
        Assert.Equal(expected, Calendar().IsBusinessTime(Sp(local)));

    [Fact]
    public void Confirmed_national_holiday_closes_the_day_but_unconfirmed_does_not()
    {
        var confirmed = Calendar([ConfirmedHoliday(new DateOnly(2026, 10, 12), "Nossa Senhora Aparecida")]);
        var unconfirmed = Calendar([new Holiday(Org, new DateOnly(2026, 10, 12), "Nossa Senhora Aparecida", HolidayScope.National, null, null, HolidaySource.Api)]);

        Assert.False(confirmed.IsBusinessTime(Sp("2026-10-12T10:00:00")));
        Assert.Equal(DayClassification.Holiday, confirmed.Classify(new LocalDate(2026, 10, 12)));
        Assert.True(unconfirmed.IsBusinessTime(Sp("2026-10-12T10:00:00")));
    }

    [Fact]
    public void State_and_city_holidays_apply_only_to_matching_location()
    {
        var holidays = new[]
        {
            ConfirmedHoliday(new DateOnly(2026, 7, 9), "Revolução Constitucionalista", HolidayScope.State, uf: "SP"),
            ConfirmedHoliday(new DateOnly(2026, 1, 25), "Aniversário de SP", HolidayScope.City, ibge: "3550308"),
        };
        var inSaoPaulo = Calendar(holidays, configure: s => s.Update("America/Sao_Paulo", "SP", "3550308", CalendarSettings.DefaultBusinessHours, CopilotCoverageRules.Default));
        var inMinas = Calendar(holidays, configure: s => s.Update("America/Sao_Paulo", "MG", "3106200", CalendarSettings.DefaultBusinessHours, CopilotCoverageRules.Default));

        Assert.True(inSaoPaulo.IsHoliday(new LocalDate(2026, 7, 9)));
        Assert.False(inMinas.IsHoliday(new LocalDate(2026, 7, 9)));
        Assert.False(inMinas.IsHoliday(new LocalDate(2026, 1, 25)));
    }

    [Fact]
    public void Special_hours_exception_overrides_weekly_hours()
    {
        var calendar = Calendar(exceptions: [new CalendarException(Org, new DateOnly(2026, 12, 24), CalendarExceptionKind.SpecialHours, new TimeOnly(8, 0), new TimeOnly(12, 0), "Véspera de Natal")]);

        Assert.True(calendar.IsBusinessTime(Sp("2026-12-24T11:59:00")));
        Assert.False(calendar.IsBusinessTime(Sp("2026-12-24T12:00:00")));
    }

    [Fact]
    public void Adding_business_days_skips_weekends_and_holidays()
    {
        var calendar = Calendar([ConfirmedHoliday(new DateOnly(2026, 10, 12), "Feriado")]);

        // Qui 08/10 + 3 dias úteis: sex 09, (sáb/dom), (seg 12 feriado), ter 13, qua 14.
        Assert.Equal(new LocalDate(2026, 10, 14), calendar.AddBusinessDays(new LocalDate(2026, 10, 8), 3));
    }

    [Theory]
    [InlineData("2026-10-08T10:00:00", CoverageMode.BusinessHours, false)]
    [InlineData("2026-10-08T18:30:00", CoverageMode.OffHours, true)]
    [InlineData("2026-10-08T07:00:00", CoverageMode.OffHours, true)]
    [InlineData("2026-10-10T10:00:00", CoverageMode.Weekend, true)]
    [InlineData("2026-10-11T03:00:00", CoverageMode.Weekend, true)]
    [InlineData("2026-10-12T10:00:00", CoverageMode.Holiday, true)]
    public void Copilot_coverage_follows_default_rules(string local, CoverageMode mode, bool mayCover)
    {
        var calendar = Calendar([ConfirmedHoliday(new DateOnly(2026, 10, 12), "Feriado")]);
        var decision = calendar.Coverage(Sp(local));

        Assert.Equal(mode, decision.Mode);
        Assert.Equal(mayCover, decision.CopilotMayCover);
    }

    [Fact]
    public void Coverage_rules_are_configurable()
    {
        var calendar = Calendar(configure: s => s.Update("America/Sao_Paulo", null, null, CalendarSettings.DefaultBusinessHours,
            new CopilotCoverageRules(OffHours: false, Saturdays: true, Sundays: false, Holidays: true)));

        Assert.False(calendar.Coverage(Sp("2026-10-08T20:00:00")).CopilotMayCover);
        Assert.True(calendar.Coverage(Sp("2026-10-10T20:00:00")).CopilotMayCover);
        Assert.False(calendar.Coverage(Sp("2026-10-11T20:00:00")).CopilotMayCover);
    }

    [Fact]
    public void Next_business_start_jumps_over_weekend()
    {
        var next = Calendar().NextBusinessStart(Sp("2026-10-09T18:30:00"));
        Assert.Equal(Sp("2026-10-12T08:00:00"), next);
    }

    [Fact]
    public void Overlapping_intervals_are_rejected() =>
        Assert.Throws<Loteadora.Crm.SharedKernel.Domain.DomainException>(() => WeeklyInterval.Validate(
        [
            new WeeklyInterval(1, new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new WeeklyInterval(1, new TimeOnly(11, 0), new TimeOnly(14, 0)),
        ]));
}
