using Loteadora.Crm.SharedKernel.Domain;

namespace Loteadora.Crm.Modules.BusinessCalendar.Domain;

/// <summary>Intervalo semiaberto [Start, End) em horário local de um dia da semana (1 = segunda … 7 = domingo, ISO-8601).</summary>
public sealed record WeeklyInterval(int IsoWeekday, TimeOnly Start, TimeOnly End)
{
    public static WeeklyInterval Create(int isoWeekday, TimeOnly start, TimeOnly end)
    {
        if (isoWeekday is < 1 or > 7)
            throw new DomainException("calendar.weekday_invalid", "Dia da semana inválido.");
        if (end <= start)
            throw new DomainException("calendar.interval_invalid", "O horário final deve ser posterior ao inicial.");
        return new WeeklyInterval(isoWeekday, start, end);
    }

    public static IReadOnlyList<WeeklyInterval> Validate(IEnumerable<WeeklyInterval> intervals)
    {
        var list = intervals.Select(i => Create(i.IsoWeekday, i.Start, i.End)).OrderBy(i => i.IsoWeekday).ThenBy(i => i.Start).ToList();
        for (var i = 1; i < list.Count; i++)
        {
            if (list[i].IsoWeekday == list[i - 1].IsoWeekday && list[i].Start < list[i - 1].End)
                throw new DomainException("calendar.interval_overlap", "Há intervalos sobrepostos no mesmo dia.");
        }
        return list;
    }
}

/// <summary>Quando o Copilot PODE atuar sem o corretor (a autonomia em si é configurada à parte).</summary>
public sealed record CopilotCoverageRules(bool OffHours, bool Saturdays, bool Sundays, bool Holidays)
{
    public static CopilotCoverageRules Default => new(OffHours: true, Saturdays: true, Sundays: true, Holidays: true);
}

/// <summary>Configuração de calendário da organização: fuso, localização (feriados), expediente e cobertura do Copilot.</summary>
public sealed class CalendarSettings : Entity, ITenantOwned, IHasTimestamps
{
    private CalendarSettings() { }

    public CalendarSettings(Guid organizationId, string timeZone, IEnumerable<WeeklyInterval> businessHours)
    {
        OrganizationId = organizationId;
        TimeZone = timeZone;
        BusinessHours = WeeklyInterval.Validate(businessHours).ToList();
    }

    public Guid OrganizationId { get; private set; }
    public string TimeZone { get; private set; } = "America/Sao_Paulo";

    /// <summary>UF para feriados estaduais (ex.: "SP").</summary>
    public string? StateCode { get; private set; }

    /// <summary>Código IBGE do município para feriados municipais.</summary>
    public string? CityIbgeCode { get; private set; }

    public List<WeeklyInterval> BusinessHours { get; private set; } = [];
    public CopilotCoverageRules Coverage { get; private set; } = CopilotCoverageRules.Default;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Padrão da especificação: segunda a sexta, 08:00–18:00.</summary>
    public static IReadOnlyList<WeeklyInterval> DefaultBusinessHours =>
        Enumerable.Range(1, 5).Select(d => new WeeklyInterval(d, new TimeOnly(8, 0), new TimeOnly(18, 0))).ToList();

    public static CalendarSettings CreateDefault(Guid organizationId) =>
        new(organizationId, "America/Sao_Paulo", DefaultBusinessHours);

    public void Update(string timeZone, string? stateCode, string? cityIbgeCode, IEnumerable<WeeklyInterval> businessHours, CopilotCoverageRules coverage)
    {
        if (NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZone) is null)
            throw new DomainException("calendar.timezone_invalid", "Fuso horário inválido.");
        if (stateCode is not null && (stateCode.Length != 2 || !stateCode.All(char.IsAsciiLetterUpper)))
            throw new DomainException("calendar.state_invalid", "UF inválida (ex.: SP).");
        if (cityIbgeCode is not null && (cityIbgeCode.Length != 7 || !cityIbgeCode.All(char.IsAsciiDigit)))
            throw new DomainException("calendar.city_invalid", "Código IBGE do município deve ter 7 dígitos.");

        TimeZone = timeZone;
        StateCode = stateCode;
        CityIbgeCode = cityIbgeCode;
        BusinessHours = WeeklyInterval.Validate(businessHours).ToList();
        Coverage = coverage;
    }
}

public enum HolidayScope
{
    National = 1,
    State = 2,
    City = 3,
}

public enum HolidaySource
{
    Manual = 1,
    Api = 2,
}

/// <summary>
/// Feriado. Importados de API entram como NÃO confirmados e só passam a valer após revisão do administrador
/// (APIs públicas podem ter lacunas, principalmente em feriados municipais).
/// </summary>
public sealed class Holiday : Entity, ITenantOwned, IHasTimestamps
{
    private Holiday() { }

    public Holiday(Guid organizationId, DateOnly date, string name, HolidayScope scope, string? stateCode, string? cityIbgeCode, HolidaySource source)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("holiday.name_required", "Informe o nome do feriado.");
        if (scope == HolidayScope.State && string.IsNullOrWhiteSpace(stateCode))
            throw new DomainException("holiday.state_required", "Feriado estadual exige UF.");
        if (scope == HolidayScope.City && string.IsNullOrWhiteSpace(cityIbgeCode))
            throw new DomainException("holiday.city_required", "Feriado municipal exige código IBGE.");

        OrganizationId = organizationId;
        Date = date;
        Name = Guard.MaxLength(name.Trim(), 120, "Nome")!;
        Scope = scope;
        StateCode = scope == HolidayScope.State ? stateCode : null;
        CityIbgeCode = scope == HolidayScope.City ? cityIbgeCode : null;
        Source = source;
        IsConfirmed = source == HolidaySource.Manual;
    }

    public Guid OrganizationId { get; private set; }
    public DateOnly Date { get; private set; }
    public string Name { get; private set; } = default!;
    public HolidayScope Scope { get; private set; }
    public string? StateCode { get; private set; }
    public string? CityIbgeCode { get; private set; }
    public HolidaySource Source { get; private set; }
    public bool IsConfirmed { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Confirm() => IsConfirmed = true;

    public bool AppliesTo(string? stateCode, string? cityIbgeCode) => Scope switch
    {
        HolidayScope.National => true,
        HolidayScope.State => StateCode is not null && StateCode == stateCode,
        HolidayScope.City => CityIbgeCode is not null && CityIbgeCode == cityIbgeCode,
        _ => false,
    };
}

public enum CalendarExceptionKind
{
    /// <summary>Organização fechada no dia.</summary>
    Closed = 1,

    /// <summary>Expediente especial no dia (substitui o horário semanal).</summary>
    SpecialHours = 2,
}

public sealed class CalendarException : Entity, ITenantOwned, IHasTimestamps
{
    private CalendarException() { }

    public CalendarException(Guid organizationId, DateOnly date, CalendarExceptionKind kind, TimeOnly? start, TimeOnly? end, string? note)
    {
        if (kind == CalendarExceptionKind.SpecialHours && (start is null || end is null || end <= start))
            throw new DomainException("calendar.exception_hours_invalid", "Expediente especial exige horário inicial e final válidos.");

        OrganizationId = organizationId;
        Date = date;
        Kind = kind;
        Start = kind == CalendarExceptionKind.SpecialHours ? start : null;
        End = kind == CalendarExceptionKind.SpecialHours ? end : null;
        Note = Guard.MaxLength(note?.Trim(), 200, "Observação");
    }

    public Guid OrganizationId { get; private set; }
    public DateOnly Date { get; private set; }
    public CalendarExceptionKind Kind { get; private set; }
    public TimeOnly? Start { get; private set; }
    public TimeOnly? End { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
