using Loteadora.Crm.Modules.BusinessCalendar.Domain;
using Loteadora.Crm.Modules.Macros.Domain;
using Loteadora.Crm.Modules.Scheduling.Domain;
using Loteadora.Crm.SharedKernel.Domain;
using NodaTime;
using NodaTime.Text;
using CalendarModel = Loteadora.Crm.Modules.BusinessCalendar.Domain.BusinessCalendar;

namespace Loteadora.Crm.Tests.Unit;

public sealed class AvailabilityCalculatorTests
{
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly DateTimeZone Zone = DateTimeZoneProviders.Tzdb["America/Sao_Paulo"];

    private static Instant Sp(string local) => LocalDateTimePattern.GeneralIso.Parse(local).Value.InZoneStrictly(Zone).ToInstant();

    private static TimeRange Range(string start, string end) => new(Sp(start), Sp(end));

    private static AgentSchedule Schedule(Action<AgentSchedule>? configure = null)
    {
        var schedule = new AgentSchedule(Org, Guid.CreateVersion7());
        schedule.Update(
            Enumerable.Range(1, 5).SelectMany(d => new[]
            {
                new WeeklyInterval(d, new TimeOnly(8, 0), new TimeOnly(12, 0)),
                new WeeklyInterval(d, new TimeOnly(13, 0), new TimeOnly(18, 0)),
            }),
            visitDurationMinutes: 60, bufferMinutes: 30, minNoticeMinutes: 120, maxVisitsPerDay: null, worksOnHolidays: false);
        configure?.Invoke(schedule);
        return schedule;
    }

    private static CalendarModel Calendar(params Holiday[] holidays) => new(CalendarSettings.CreateDefault(Org), holidays, []);

    private static AvailabilityCalculator Calc(AgentSchedule schedule, IReadOnlyList<BusyItem>? busy = null, string now = "2026-10-08T07:00:00", CalendarModel? calendar = null) =>
        new(schedule, calendar ?? Calendar(), busy ?? [], Sp(now));

    [Fact]
    public void Agent_without_work_blocks_has_no_availability()
    {
        var empty = new AgentSchedule(Org, Guid.CreateVersion7());
        var slots = Calc(empty).FreeSlots(new LocalDate(2026, 10, 8), new LocalDate(2026, 10, 15), Duration.FromHours(1), Duration.FromMinutes(30));
        Assert.Empty(slots);
    }

    [Fact]
    public void Free_slots_respect_lunch_break_min_notice_and_buffer()
    {
        var busy = new[] { new BusyItem(Range("2026-10-08T14:00:00", "2026-10-08T15:00:00"), BusyKind.Appointment, true) };
        var slots = Calc(Schedule(), busy, now: "2026-10-08T08:00:00")
            .FreeSlots(new LocalDate(2026, 10, 8), new LocalDate(2026, 10, 8), Duration.FromHours(1), Duration.FromMinutes(30))
            .Select(s => s.Start.InZone(Zone).TimeOfDay.ToString("HH:mm", null))
            .ToList();

        // Antecedência de 2h => a partir das 10:00; almoço 12–13; visita 14–15 com folga de 30 min bloqueia 12:30–15:30.
        Assert.Equal(["10:00", "10:30", "11:00", "15:30", "16:00", "16:30", "17:00"], slots);
    }

    [Fact]
    public void Conflict_and_absence_are_blocking_issues()
    {
        var busy = new[]
        {
            new BusyItem(Range("2026-10-09T10:00:00", "2026-10-09T11:00:00"), BusyKind.Appointment, true),
            new BusyItem(Range("2026-10-09T14:00:00", "2026-10-09T18:00:00"), BusyKind.Absence, false),
        };
        var calc = Calc(Schedule(), busy);

        var conflict = calc.Check(Range("2026-10-09T10:30:00", "2026-10-09T11:30:00"), isVisit: true);
        var absence = calc.Check(Range("2026-10-09T15:00:00", "2026-10-09T16:00:00"), isVisit: true);

        Assert.Contains(SlotIssue.Conflict, conflict.Issues);
        Assert.True(conflict.HasBlockingIssue);
        Assert.Contains(SlotIssue.Absence, absence.Issues);
        Assert.True(absence.HasBlockingIssue);
    }

    [Fact]
    public void Outside_hours_and_holiday_are_warnings_not_blocking()
    {
        var holiday = new Holiday(Org, new DateOnly(2026, 10, 12), "Feriado", HolidayScope.National, null, null, HolidaySource.Manual);
        var calc = Calc(Schedule(), calendar: Calendar(holiday));

        var evening = calc.Check(Range("2026-10-09T19:00:00", "2026-10-09T20:00:00"), isVisit: true);
        var onHoliday = calc.Check(Range("2026-10-12T10:00:00", "2026-10-12T11:00:00"), isVisit: true);

        Assert.Contains(SlotIssue.OutsideWorkingHours, evening.Issues);
        Assert.False(evening.HasBlockingIssue);
        Assert.Contains(SlotIssue.Holiday, onHoliday.Issues);
        Assert.False(onHoliday.HasBlockingIssue);
    }

    [Fact]
    public void Max_visits_per_day_is_enforced()
    {
        var schedule = Schedule(s => s.Update(s.WorkBlocks, 60, 0, 0, maxVisitsPerDay: 1, worksOnHolidays: false));
        var busy = new[] { new BusyItem(Range("2026-10-09T09:00:00", "2026-10-09T10:00:00"), BusyKind.Appointment, true) };

        var check = Calc(schedule, busy).Check(Range("2026-10-09T15:00:00", "2026-10-09T16:00:00"), isVisit: true);
        Assert.Contains(SlotIssue.MaxVisitsReached, check.Issues);
    }

    [Fact]
    public void Past_slot_is_blocking()
    {
        var check = Calc(Schedule(), now: "2026-10-08T12:00:00").Check(Range("2026-10-08T09:00:00", "2026-10-08T10:00:00"), isVisit: true);
        Assert.Contains(SlotIssue.InPast, check.Issues);
        Assert.True(check.HasBlockingIssue);
    }
}

public sealed class MacroTemplateTests
{
    private static readonly IReadOnlyDictionary<string, string?> Values = new Dictionary<string, string?>
    {
        ["lead.first_name"] = "João",
        ["agent.name"] = "Marcos Corretor",
        ["org.name"] = "Loteadora Demo",
    };

    [Fact]
    public void Renders_whitelisted_variables_and_reports_missing()
    {
        var result = MacroTemplate.Render("Olá, {{lead.first_name}}! Sou {{ agent.name }} da {{org.name}}. Interesse: {{lead.interest}}", Values);

        Assert.Equal("Olá, João! Sou Marcos Corretor da Loteadora Demo. Interesse: ", result.Text);
        Assert.Equal(["lead.interest"], result.MissingVariables);
    }

    [Fact]
    public void Unknown_variables_are_rejected_on_save()
    {
        var ex = Assert.Throws<DomainException>(() => new Macro(Guid.NewGuid(), null, "/teste", "Teste", "Seu CPF é {{lead.cpf}}", null));
        Assert.Equal("macro.unknown_variables", ex.Code);
    }

    [Fact]
    public void Values_are_inserted_literally_without_reevaluation()
    {
        var values = new Dictionary<string, string?> { ["lead.first_name"] = "{{agent.name}}" };
        var result = MacroTemplate.Render("Oi {{lead.first_name}}", values);
        Assert.Equal("Oi {{agent.name}}", result.Text);
    }

    [Theory]
    [InlineData("apresentacao", "/apresentacao")]
    [InlineData("/Valores", "/valores")]
    public void Shortcut_is_normalized(string input, string expected) =>
        Assert.Equal(expected, new Macro(Guid.NewGuid(), null, input, "T", "Texto", null).Shortcut);

    [Theory]
    [InlineData("/a")]
    [InlineData("/com espaço")]
    [InlineData("/ação")]
    public void Invalid_shortcuts_are_rejected(string input) =>
        Assert.Throws<DomainException>(() => new Macro(Guid.NewGuid(), null, input, "T", "Texto", null));
}
