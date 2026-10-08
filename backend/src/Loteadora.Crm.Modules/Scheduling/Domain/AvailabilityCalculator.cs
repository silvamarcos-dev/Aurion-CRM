using Loteadora.Crm.Modules.BusinessCalendar.Domain;
using NodaTime;
using CalendarModel = Loteadora.Crm.Modules.BusinessCalendar.Domain.BusinessCalendar;

namespace Loteadora.Crm.Modules.Scheduling.Domain;

public sealed record TimeRange(Instant Start, Instant End)
{
    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;

    public bool Contains(TimeRange other) => Start <= other.Start && other.End <= End;
}

public enum BusyKind
{
    Appointment = 1,
    Absence = 2,
}

public sealed record BusyItem(TimeRange Range, BusyKind Kind, bool IsVisit);

public enum SlotIssue
{
    /// <summary>Bloqueante: sobreposição direta com outro compromisso.</summary>
    Conflict = 1,

    /// <summary>Bloqueante: corretor ausente (férias, folga, bloqueio).</summary>
    Absence = 2,

    /// <summary>Bloqueante: horário no passado.</summary>
    InPast = 3,

    OutsideWorkingHours = 4,
    BufferViolation = 5,
    MinNotice = 6,
    Holiday = 7,
    MaxVisitsReached = 8,
}

public sealed record SlotCheck(IReadOnlyList<SlotIssue> Issues)
{
    public bool IsFree => Issues.Count == 0;

    public bool HasBlockingIssue => Issues.Any(i => i is SlotIssue.Conflict or SlotIssue.Absence or SlotIssue.InPast);

    public IReadOnlyList<SlotIssue> Warnings => Issues.Where(i => i is not (SlotIssue.Conflict or SlotIssue.Absence or SlotIssue.InPast)).ToList();
}

/// <summary>
/// Cálculo DETERMINÍSTICO da disponibilidade real do corretor:
/// jornada − feriados/fechamentos − ausências − compromissos (com folga) − antecedência mínima − limite diário.
/// Sem I/O. É a mesma lógica que o Copilot usará na tool calendar.get_availability — nunca se inventa horário.
/// </summary>
public sealed class AvailabilityCalculator(AgentSchedule schedule, CalendarModel calendar, IReadOnlyList<BusyItem> busy, Instant now)
{
    private readonly Duration _buffer = Duration.FromMinutes(schedule.BufferMinutes);

    public IReadOnlyList<TimeRange> WorkWindows(LocalDate date)
    {
        if (calendar.Classify(date) == DayClassification.Closed)
            return [];
        if (calendar.IsHoliday(date) && !schedule.WorksOnHolidays)
            return [];

        return schedule.WorkBlocks
            .Where(b => b.IsoWeekday == (int)date.DayOfWeek)
            .Select(b => new TimeRange(
                (date + b.Start.ToLocalTime()).InZoneLeniently(calendar.Zone).ToInstant(),
                (date + b.End.ToLocalTime()).InZoneLeniently(calendar.Zone).ToInstant()))
            .ToList();
    }

    public SlotCheck Check(TimeRange slot, bool isVisit)
    {
        var issues = new List<SlotIssue>();
        var date = slot.Start.InZone(calendar.Zone).Date;

        if (slot.Start < now)
            issues.Add(SlotIssue.InPast);
        else if (slot.Start < now + Duration.FromMinutes(schedule.MinNoticeMinutes))
            issues.Add(SlotIssue.MinNotice);

        if (calendar.IsHoliday(date) && !schedule.WorksOnHolidays)
            issues.Add(SlotIssue.Holiday);

        if (!WorkWindows(date).Any(w => w.Contains(slot)))
            issues.Add(SlotIssue.OutsideWorkingHours);

        if (busy.Any(b => b.Kind == BusyKind.Absence && b.Range.Overlaps(slot)))
            issues.Add(SlotIssue.Absence);

        var appointments = busy.Where(b => b.Kind == BusyKind.Appointment).ToList();
        if (appointments.Any(b => b.Range.Overlaps(slot)))
            issues.Add(SlotIssue.Conflict);
        else if (appointments.Any(b => new TimeRange(b.Range.Start - _buffer, b.Range.End + _buffer).Overlaps(slot)))
            issues.Add(SlotIssue.BufferViolation);

        if (isVisit && schedule.MaxVisitsPerDay is { } max)
        {
            var dayStart = date.AtStartOfDayInZone(calendar.Zone).ToInstant();
            var dayEnd = date.PlusDays(1).AtStartOfDayInZone(calendar.Zone).ToInstant();
            if (appointments.Count(b => b.IsVisit && b.Range.Start >= dayStart && b.Range.Start < dayEnd) >= max)
                issues.Add(SlotIssue.MaxVisitsReached);
        }

        return new SlotCheck(issues);
    }

    /// <summary>Horários totalmente livres entre as datas (inclusive), no passo informado.</summary>
    public IReadOnlyList<TimeRange> FreeSlots(LocalDate from, LocalDate to, Duration duration, Duration step, int limit = 200)
    {
        var slots = new List<TimeRange>();
        for (var date = from; date <= to && slots.Count < limit; date = date.PlusDays(1))
        {
            foreach (var window in WorkWindows(date).OrderBy(w => w.Start))
            {
                for (var start = window.Start; start + duration <= window.End && slots.Count < limit; start += step)
                {
                    var slot = new TimeRange(start, start + duration);
                    if (Check(slot, isVisit: true).IsFree)
                        slots.Add(slot);
                }
            }
        }
        return slots;
    }
}
