using NodaTime;

namespace Loteadora.Crm.Modules.BusinessCalendar.Domain;

public enum DayClassification
{
    Weekday = 1,
    Saturday = 2,
    Sunday = 3,
    Holiday = 4,
    Closed = 5,
    SpecialHours = 6,
}

public enum CoverageMode
{
    /// <summary>Expediente: o corretor é o responsável prioritário (Copilot só via regra de takeover).</summary>
    BusinessHours = 1,
    OffHours = 2,
    Weekend = 3,
    Holiday = 4,
    Closed = 5,
}

/// <param name="CopilotMayCover">A janela permite atuação do Copilot sem o corretor (sujeito ainda à autonomia e políticas).</param>
public sealed record CoverageDecision(CoverageMode Mode, bool CopilotMayCover, string Reason);

/// <summary>Período local de trabalho já resolvido para uma data.</summary>
public sealed record LocalWindow(LocalTime Start, LocalTime End);

/// <summary>
/// Núcleo PURO do calendário comercial (sem I/O). Toda regra de expediente, feriado, exceção
/// e cobertura do Copilot fica aqui — o restante do sistema nunca compara horários diretamente.
/// Intervalos são semiabertos [início, fim): 18:00 já é fora do expediente que termina às 18:00.
/// </summary>
public sealed class BusinessCalendar
{
    private readonly ILookup<int, WeeklyInterval> _weekly;
    private readonly Dictionary<LocalDate, Holiday> _holidays;
    private readonly Dictionary<LocalDate, CalendarException> _exceptions;

    public BusinessCalendar(CalendarSettings settings, IEnumerable<Holiday> holidays, IEnumerable<CalendarException> exceptions)
    {
        Settings = settings;
        Zone = DateTimeZoneProviders.Tzdb[settings.TimeZone];
        _weekly = settings.BusinessHours.ToLookup(i => i.IsoWeekday);
        _holidays = holidays
            .Where(h => h.IsConfirmed && h.AppliesTo(settings.StateCode, settings.CityIbgeCode))
            .GroupBy(h => h.Date.ToLocalDate())
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.Scope).First());
        _exceptions = exceptions.GroupBy(e => e.Date.ToLocalDate()).ToDictionary(g => g.Key, g => g.Last());
    }

    public CalendarSettings Settings { get; }
    public DateTimeZone Zone { get; }

    public Holiday? HolidayOn(LocalDate date) => _holidays.GetValueOrDefault(date);

    public bool IsHoliday(LocalDate date) => _holidays.ContainsKey(date);

    public DayClassification Classify(LocalDate date)
    {
        if (_exceptions.TryGetValue(date, out var exception))
            return exception.Kind == CalendarExceptionKind.Closed ? DayClassification.Closed : DayClassification.SpecialHours;
        if (IsHoliday(date))
            return DayClassification.Holiday;
        return date.DayOfWeek switch
        {
            IsoDayOfWeek.Saturday => DayClassification.Saturday,
            IsoDayOfWeek.Sunday => DayClassification.Sunday,
            _ => DayClassification.Weekday,
        };
    }

    /// <summary>Janelas de expediente da organização naquela data (exceção &gt; feriado &gt; horário semanal).</summary>
    public IReadOnlyList<LocalWindow> BusinessWindows(LocalDate date)
    {
        if (_exceptions.TryGetValue(date, out var exception))
        {
            return exception.Kind == CalendarExceptionKind.SpecialHours
                ? [new LocalWindow(exception.Start!.Value.ToLocalTime(), exception.End!.Value.ToLocalTime())]
                : [];
        }

        if (IsHoliday(date))
            return [];

        return _weekly[(int)date.DayOfWeek].Select(i => new LocalWindow(i.Start.ToLocalTime(), i.End.ToLocalTime())).ToList();
    }

    public bool IsBusinessDay(LocalDate date) => BusinessWindows(date).Count > 0;

    public bool IsBusinessTime(Instant instant)
    {
        var local = instant.InZone(Zone).LocalDateTime;
        return BusinessWindows(local.Date).Any(w => local.TimeOfDay >= w.Start && local.TimeOfDay < w.End);
    }

    /// <summary>Soma N dias úteis (N &gt;= 0) a partir da data informada (a própria data não conta).</summary>
    public LocalDate AddBusinessDays(LocalDate from, int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);
        var current = from;
        var guard = 0;
        while (days > 0)
        {
            current = current.PlusDays(1);
            if (IsBusinessDay(current))
                days--;
            if (++guard > 3660)
                throw new InvalidOperationException("Calendário sem dias úteis configurados.");
        }
        return current;
    }

    /// <summary>Próximo instante de expediente a partir de <paramref name="from"/> (ele próprio, se já for expediente).</summary>
    public Instant? NextBusinessStart(Instant from)
    {
        var local = from.InZone(Zone).LocalDateTime;
        for (var i = 0; i < 366; i++)
        {
            var date = local.Date.PlusDays(i);
            foreach (var window in BusinessWindows(date).OrderBy(w => w.Start))
            {
                if (i == 0 && local.TimeOfDay >= window.End)
                    continue;
                var start = i == 0 && local.TimeOfDay > window.Start ? local.TimeOfDay : window.Start;
                return (date + start).InZoneLeniently(Zone).ToInstant();
            }
        }
        return null;
    }

    /// <summary>Determina se a janela atual permite atuação do Copilot, conforme a política da organização.</summary>
    public CoverageDecision Coverage(Instant instant)
    {
        var rules = Settings.Coverage;
        var date = instant.InZone(Zone).Date;

        if (IsBusinessTime(instant))
            return new CoverageDecision(CoverageMode.BusinessHours, false, "Horário comercial: o corretor é o responsável prioritário.");

        return Classify(date) switch
        {
            DayClassification.Holiday => new CoverageDecision(CoverageMode.Holiday, rules.Holidays, $"Feriado: {HolidayOn(date)!.Name}."),
            DayClassification.Saturday when !IsBusinessDay(date) => new CoverageDecision(CoverageMode.Weekend, rules.Saturdays, "Sábado sem expediente."),
            DayClassification.Sunday when !IsBusinessDay(date) => new CoverageDecision(CoverageMode.Weekend, rules.Sundays, "Domingo sem expediente."),
            DayClassification.Closed => new CoverageDecision(CoverageMode.Closed, rules.OffHours, "Organização fechada (exceção de calendário)."),
            _ => new CoverageDecision(CoverageMode.OffHours, rules.OffHours, "Fora do horário comercial."),
        };
    }
}

public static class CalendarConversions
{
    public static LocalDate ToLocalDate(this DateOnly date) => new(date.Year, date.Month, date.Day);

    public static DateOnly ToDateOnly(this LocalDate date) => new(date.Year, date.Month, date.Day);

    public static LocalTime ToLocalTime(this TimeOnly time) => new(time.Hour, time.Minute, time.Second);

    public static TimeOnly ToTimeOnly(this LocalTime time) => new(time.Hour, time.Minute, time.Second);

    public static Instant ToInstant(this DateTimeOffset value) => Instant.FromDateTimeOffset(value);
}
