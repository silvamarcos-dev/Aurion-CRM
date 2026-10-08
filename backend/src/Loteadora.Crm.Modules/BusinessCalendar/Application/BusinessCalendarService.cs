using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.BusinessCalendar.Domain;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Loteadora.Crm.Modules.BusinessCalendar.Application;

public sealed record CalendarSettingsDto(
    string TimeZone,
    string? StateCode,
    string? CityIbgeCode,
    IReadOnlyList<WeeklyInterval> BusinessHours,
    CopilotCoverageRules Coverage);

public sealed record HolidayDto(Guid Id, DateOnly Date, string Name, HolidayScope Scope, string? StateCode, string? CityIbgeCode, HolidaySource Source, bool IsConfirmed, bool AppliesToOrganization);

public sealed record CreateHolidayCommand(DateOnly Date, string Name, HolidayScope Scope, string? StateCode, string? CityIbgeCode);

public sealed record CalendarExceptionDto(Guid Id, DateOnly Date, CalendarExceptionKind Kind, TimeOnly? Start, TimeOnly? End, string? Note);

public sealed record CreateCalendarExceptionCommand(DateOnly Date, CalendarExceptionKind Kind, TimeOnly? Start, TimeOnly? End, string? Note);

public sealed record CalendarStatusDto(DateTimeOffset At, bool IsBusinessTime, string DayClassification, CoverageMode CoverageMode, bool CopilotMayCover, string Reason, DateTimeOffset? NextBusinessStart);

public sealed record ImportHolidaysResult(int Year, string Provider, int Imported, int AlreadyPresent);

/// <summary>
/// Fachada do calendário comercial da organização corrente. Carrega a configuração e devolve
/// um <see cref="Domain.BusinessCalendar"/> puro para cálculos. Única porta de entrada para regras de horário.
/// </summary>
public sealed class BusinessCalendarService(AppDbContext db, ICurrentActor actor, IAuditTrail audit, IHolidayProvider holidayProvider, TimeProvider clock)
{
    public async Task<Domain.BusinessCalendar> LoadAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var settings = await db.CalendarSettings.AsNoTracking().FirstOrDefaultAsync(ct)
                       ?? CalendarSettings.CreateDefault(actor.OrganizationId);
        var holidays = await db.Holidays.AsNoTracking().Where(h => h.Date >= from && h.Date <= to && h.IsConfirmed).ToListAsync(ct);
        var exceptions = await db.CalendarExceptions.AsNoTracking().Where(e => e.Date >= from && e.Date <= to).ToListAsync(ct);
        return new Domain.BusinessCalendar(settings, holidays, exceptions);
    }

    /// <summary>Calendário cobrindo o ano anterior, o corrente e o seguinte em relação a <paramref name="around"/>.</summary>
    public Task<Domain.BusinessCalendar> LoadAroundAsync(DateTimeOffset around, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(around.UtcDateTime);
        return LoadAsync(date.AddYears(-1), date.AddYears(1), ct);
    }

    public async Task<CalendarStatusDto> GetStatusAsync(DateTimeOffset? at, CancellationToken ct)
    {
        var when = at ?? clock.GetUtcNow();
        var calendar = await LoadAroundAsync(when, ct);
        var instant = when.ToInstant();
        var coverage = calendar.Coverage(instant);
        var next = calendar.NextBusinessStart(instant);
        return new CalendarStatusDto(when, calendar.IsBusinessTime(instant), calendar.Classify(instant.InZone(calendar.Zone).Date).ToString(),
            coverage.Mode, coverage.CopilotMayCover, coverage.Reason, next?.ToDateTimeOffset());
    }

    public async Task<CalendarSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        actor.Require(Capabilities.CalendarRead);
        var s = await db.CalendarSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? CalendarSettings.CreateDefault(actor.OrganizationId);
        return new CalendarSettingsDto(s.TimeZone, s.StateCode, s.CityIbgeCode, s.BusinessHours, s.Coverage);
    }

    public async Task UpdateSettingsAsync(CalendarSettingsDto dto, CancellationToken ct)
    {
        actor.Require(Capabilities.SettingsManage);
        var settings = await db.CalendarSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = CalendarSettings.CreateDefault(actor.OrganizationId);
            db.CalendarSettings.Add(settings);
        }

        settings.Update(dto.TimeZone, Normalize(dto.StateCode)?.ToUpperInvariant(), Normalize(dto.CityIbgeCode), dto.BusinessHours, dto.Coverage);
        audit.Record("calendar.settings_updated", "calendar_settings", settings.Id, new { dto.TimeZone, dto.StateCode, dto.CityIbgeCode, dto.BusinessHours, dto.Coverage });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(int year, CancellationToken ct)
    {
        actor.Require(Capabilities.CalendarRead);
        var settings = await db.CalendarSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? CalendarSettings.CreateDefault(actor.OrganizationId);
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var holidays = await db.Holidays.AsNoTracking().Where(h => h.Date >= from && h.Date <= to).OrderBy(h => h.Date).ToListAsync(ct);
        return holidays.Select(h => new HolidayDto(h.Id, h.Date, h.Name, h.Scope, h.StateCode, h.CityIbgeCode, h.Source, h.IsConfirmed,
            h.AppliesTo(settings.StateCode, settings.CityIbgeCode))).ToList();
    }

    public async Task<Guid> CreateHolidayAsync(CreateHolidayCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.SettingsManage);
        var holiday = new Holiday(actor.OrganizationId, cmd.Date, cmd.Name, cmd.Scope, Normalize(cmd.StateCode)?.ToUpperInvariant(), Normalize(cmd.CityIbgeCode), HolidaySource.Manual);

        if (await db.Holidays.AnyAsync(h => h.Date == holiday.Date && h.Scope == holiday.Scope && h.StateCode == holiday.StateCode && h.CityIbgeCode == holiday.CityIbgeCode, ct))
            throw new DomainException("holiday.duplicate", "Já existe um feriado com esta data e abrangência.");

        db.Holidays.Add(holiday);
        audit.Record("calendar.holiday_created", "holiday", holiday.Id, new { holiday.Date, holiday.Name, scope = holiday.Scope.ToString() });
        await db.SaveChangesAsync(ct);
        return holiday.Id;
    }

    public async Task ConfirmHolidayAsync(Guid id, CancellationToken ct)
    {
        actor.Require(Capabilities.SettingsManage);
        var holiday = await db.Holidays.FirstOrDefaultAsync(h => h.Id == id, ct) ?? throw new NotFoundException("Feriado");
        holiday.Confirm();
        audit.Record("calendar.holiday_confirmed", "holiday", holiday.Id, new { holiday.Date, holiday.Name });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteHolidayAsync(Guid id, CancellationToken ct)
    {
        actor.Require(Capabilities.SettingsManage);
        var holiday = await db.Holidays.FirstOrDefaultAsync(h => h.Id == id, ct) ?? throw new NotFoundException("Feriado");
        db.Holidays.Remove(holiday);
        audit.Record("calendar.holiday_deleted", "holiday", holiday.Id, new { holiday.Date, holiday.Name });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Importa feriados nacionais do provedor externo como NÃO confirmados (exigem revisão).</summary>
    public async Task<ImportHolidaysResult> ImportNationalHolidaysAsync(int year, CancellationToken ct)
    {
        actor.Require(Capabilities.SettingsManage);
        var external = await holidayProvider.GetNationalHolidaysAsync(year, ct);
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var existingDates = (await db.Holidays.Where(h => h.Scope == HolidayScope.National && h.Date >= from && h.Date <= to)
            .Select(h => h.Date).ToListAsync(ct)).ToHashSet();

        var imported = 0;
        foreach (var item in external.Where(e => !existingDates.Contains(e.Date)))
        {
            db.Holidays.Add(new Holiday(actor.OrganizationId, item.Date, item.Name, HolidayScope.National, null, null, HolidaySource.Api));
            imported++;
        }

        audit.Record("calendar.holidays_imported", "calendar_settings", null, new { year, provider = holidayProvider.Name, imported });
        await db.SaveChangesAsync(ct);
        return new ImportHolidaysResult(year, holidayProvider.Name, imported, external.Count - imported);
    }

    public async Task<IReadOnlyList<CalendarExceptionDto>> ListExceptionsAsync(int year, CancellationToken ct)
    {
        actor.Require(Capabilities.CalendarRead);
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        return await db.CalendarExceptions.AsNoTracking().Where(e => e.Date >= from && e.Date <= to).OrderBy(e => e.Date)
            .Select(e => new CalendarExceptionDto(e.Id, e.Date, e.Kind, e.Start, e.End, e.Note)).ToListAsync(ct);
    }

    public async Task<Guid> CreateExceptionAsync(CreateCalendarExceptionCommand cmd, CancellationToken ct)
    {
        actor.Require(Capabilities.SettingsManage);
        if (await db.CalendarExceptions.AnyAsync(e => e.Date == cmd.Date, ct))
            throw new DomainException("calendar.exception_duplicate", "Já existe uma exceção para esta data.");

        var exception = new CalendarException(actor.OrganizationId, cmd.Date, cmd.Kind, cmd.Start, cmd.End, cmd.Note);
        db.CalendarExceptions.Add(exception);
        audit.Record("calendar.exception_created", "calendar_exception", exception.Id, new { cmd.Date, kind = cmd.Kind.ToString(), cmd.Start, cmd.End });
        await db.SaveChangesAsync(ct);
        return exception.Id;
    }

    public async Task DeleteExceptionAsync(Guid id, CancellationToken ct)
    {
        actor.Require(Capabilities.SettingsManage);
        var exception = await db.CalendarExceptions.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Exceção");
        db.CalendarExceptions.Remove(exception);
        audit.Record("calendar.exception_deleted", "calendar_exception", exception.Id, new { exception.Date });
        await db.SaveChangesAsync(ct);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
