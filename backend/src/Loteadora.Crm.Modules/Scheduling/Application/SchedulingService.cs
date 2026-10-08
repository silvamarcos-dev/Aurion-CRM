using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.BusinessCalendar.Application;
using Loteadora.Crm.Modules.BusinessCalendar.Domain;
using Loteadora.Crm.Modules.Crm.Application;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.Modules.Scheduling.Domain;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using CalendarModel = Loteadora.Crm.Modules.BusinessCalendar.Domain.BusinessCalendar;

namespace Loteadora.Crm.Modules.Scheduling.Application;

public sealed record AgentScheduleDto(Guid UserId, bool IsConfigured, IReadOnlyList<WeeklyInterval> WorkBlocks, int VisitDurationMinutes, int BufferMinutes, int MinNoticeMinutes, int? MaxVisitsPerDay, bool WorksOnHolidays);

public sealed record UpdateAgentScheduleCommand(IReadOnlyList<WeeklyInterval> WorkBlocks, int VisitDurationMinutes, int BufferMinutes, int MinNoticeMinutes, int? MaxVisitsPerDay, bool WorksOnHolidays);

public sealed record AbsenceDto(Guid Id, Guid UserId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, AbsenceKind Kind, string? Note);

public sealed record CreateAbsenceCommand(Guid? UserId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, AbsenceKind Kind, string? Note);

public sealed record AppointmentDto(Guid Id, Guid AgentUserId, string AgentName, Guid? LeadId, string? LeadName, AppointmentKind Kind, string Title,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Location, string? Notes, AppointmentStatus Status, string? CancelReason, string CreatedByKind, uint Version);

public sealed record CreateAppointmentCommand(Guid? AgentUserId, Guid? LeadId, AppointmentKind Kind, string? Title, DateTimeOffset StartsAt, DateTimeOffset? EndsAt,
    string? Location, string? Notes, bool ConfirmWarnings);

public sealed record RescheduleAppointmentCommand(string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Location, string? Notes, bool ConfirmWarnings, uint ExpectedVersion);

public sealed record CancelAppointmentCommand(string? Reason);

public sealed record CompleteAppointmentCommand(bool Attended);

public sealed record AgendaDto(Guid UserId, string TimeZone, AgentScheduleDto Schedule, IReadOnlyList<AppointmentDto> Appointments, IReadOnlyList<AbsenceDto> Absences, IReadOnlyList<AgendaDayDto> Days);

public sealed record AgendaDayDto(DateOnly Date, string Classification, string? HolidayName, IReadOnlyList<RangeDto> WorkWindows);

public sealed record RangeDto(DateTimeOffset Start, DateTimeOffset End);

public sealed record AvailabilityDto(Guid UserId, bool IsScheduleConfigured, int DurationMinutes, IReadOnlyList<RangeDto> Slots);

public sealed record SlotCheckDto(bool IsFree, bool HasBlockingIssue, IReadOnlyList<string> Issues);

/// <summary>
/// Agenda do corretor. Toda verificação de disponibilidade passa pelo <see cref="AvailabilityCalculator"/>;
/// a sobreposição de compromissos é garantida em última instância pelo banco (EXCLUDE constraint).
/// </summary>
public sealed class SchedulingService(
    AppDbContext db,
    ICurrentActor actor,
    IAuditTrail audit,
    MemberScope members,
    LeadAccess leads,
    BusinessCalendarService calendars,
    PiiMask mask,
    TimeProvider clock)
{
    private static readonly Duration SlotStep = Duration.FromMinutes(30);

    public static readonly IReadOnlyDictionary<SlotIssue, string> IssueMessages = new Dictionary<SlotIssue, string>
    {
        [SlotIssue.Conflict] = "Conflita com outro compromisso do corretor.",
        [SlotIssue.Absence] = "O corretor está ausente neste horário.",
        [SlotIssue.InPast] = "O horário já passou.",
        [SlotIssue.OutsideWorkingHours] = "Fora da jornada de trabalho do corretor.",
        [SlotIssue.BufferViolation] = "Sem o intervalo mínimo em relação a outro compromisso.",
        [SlotIssue.MinNotice] = "Abaixo da antecedência mínima configurada.",
        [SlotIssue.Holiday] = "Feriado (o corretor não trabalha em feriados).",
        [SlotIssue.MaxVisitsReached] = "Limite diário de visitas atingido.",
    };

    // ───────────────────────── Jornada ─────────────────────────

    public async Task<AgentScheduleDto> GetScheduleAsync(Guid? userId, CancellationToken ct)
    {
        var target = await members.RequireUserAsync(Capabilities.CalendarRead, userId, ct);
        var schedule = await db.AgentSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == target, ct);
        return ToDto(target, schedule);
    }

    public async Task UpdateScheduleAsync(Guid? userId, UpdateAgentScheduleCommand cmd, CancellationToken ct)
    {
        var target = await members.RequireUserAsync(Capabilities.CalendarManage, userId, ct);
        var schedule = await db.AgentSchedules.FirstOrDefaultAsync(s => s.UserId == target, ct);
        if (schedule is null)
        {
            schedule = new AgentSchedule(actor.OrganizationId, target);
            db.AgentSchedules.Add(schedule);
        }

        schedule.Update(cmd.WorkBlocks, cmd.VisitDurationMinutes, cmd.BufferMinutes, cmd.MinNoticeMinutes, cmd.MaxVisitsPerDay, cmd.WorksOnHolidays);
        audit.Record("schedule.updated", "user", target, new { cmd.WorkBlocks, cmd.VisitDurationMinutes, cmd.BufferMinutes, cmd.MinNoticeMinutes, cmd.MaxVisitsPerDay, cmd.WorksOnHolidays });
        await db.SaveChangesAsync(ct);
    }

    // ───────────────────────── Ausências ─────────────────────────

    public async Task<Guid> CreateAbsenceAsync(CreateAbsenceCommand cmd, CancellationToken ct)
    {
        var target = await members.RequireUserAsync(Capabilities.CalendarManage, cmd.UserId, ct);
        var absence = new Absence(actor.OrganizationId, target, cmd.StartsAt.ToUniversalTime(), cmd.EndsAt.ToUniversalTime(), cmd.Kind, cmd.Note);
        db.Absences.Add(absence);
        audit.Record("schedule.absence_created", "user", target, new { absenceId = absence.Id, cmd.StartsAt, cmd.EndsAt, kind = cmd.Kind.ToString() });
        await db.SaveChangesAsync(ct);
        return absence.Id;
    }

    public async Task DeleteAbsenceAsync(Guid id, CancellationToken ct)
    {
        var absence = await db.Absences.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Ausência");
        await members.RequireUserAsync(Capabilities.CalendarManage, absence.UserId, ct);
        db.Absences.Remove(absence);
        audit.Record("schedule.absence_deleted", "user", absence.UserId, new { absenceId = id });
        await db.SaveChangesAsync(ct);
    }

    // ───────────────────────── Agenda / disponibilidade ─────────────────────────

    public async Task<AgendaDto> GetAgendaAsync(Guid? userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 62)
            throw new DomainException("agenda.range_invalid", "Período inválido (máximo de 62 dias).");

        var target = await members.RequireUserAsync(Capabilities.CalendarRead, userId, ct);
        var calendar = await calendars.LoadAsync(from.AddDays(-1), to.AddDays(1), ct);
        var (start, end) = Bounds(calendar, from, to);
        var schedule = await db.AgentSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == target, ct);

        var appointments = (await AppointmentsQuery(a => a.AgentUserId == target && a.StartsAt < end && a.EndsAt > start).ToListAsync(ct))
            .OrderBy(a => a.StartsAt).Select(Mask).ToList();
        var absences = await db.Absences.AsNoTracking().Where(a => a.UserId == target && a.StartsAt < end && a.EndsAt > start)
            .Select(a => new AbsenceDto(a.Id, a.UserId, a.StartsAt, a.EndsAt, a.Kind, a.Note)).ToListAsync(ct);

        var calculator = new AvailabilityCalculator(schedule ?? new AgentSchedule(actor.OrganizationId, target), calendar, [], Now());
        var days = new List<AgendaDayDto>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var local = d.ToLocalDate();
            days.Add(new AgendaDayDto(d, calendar.Classify(local).ToString(), calendar.HolidayOn(local)?.Name,
                calculator.WorkWindows(local).Select(w => new RangeDto(w.Start.ToDateTimeOffset(), w.End.ToDateTimeOffset())).ToList()));
        }

        return new AgendaDto(target, calendar.Settings.TimeZone, ToDto(target, schedule), appointments, absences, days);
    }

    public async Task<AvailabilityDto> GetAvailabilityAsync(Guid? userId, DateOnly from, DateOnly to, int? durationMinutes, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 31)
            throw new DomainException("availability.range_invalid", "Período inválido (máximo de 31 dias).");

        var target = await members.RequireUserAsync(Capabilities.CalendarRead, userId, ct);
        var (calculator, schedule) = await BuildCalculatorAsync(target, from, to, ignoreAppointmentId: null, ct);
        if (schedule is null)
            return new AvailabilityDto(target, false, durationMinutes ?? 60, []);

        var duration = Duration.FromMinutes(durationMinutes ?? schedule.VisitDurationMinutes);
        var slots = calculator.FreeSlots(from.ToLocalDate(), to.ToLocalDate(), duration, SlotStep);
        return new AvailabilityDto(target, true, (int)duration.TotalMinutes,
            slots.Select(s => new RangeDto(s.Start.ToDateTimeOffset(), s.End.ToDateTimeOffset())).ToList());
    }

    public async Task<SlotCheckDto> CheckSlotAsync(Guid? userId, DateTimeOffset startsAt, DateTimeOffset endsAt, bool isVisit, CancellationToken ct)
    {
        var target = await members.RequireUserAsync(Capabilities.CalendarRead, userId, ct);
        var check = await CheckAsync(target, startsAt, endsAt, isVisit, ignoreAppointmentId: null, ct);
        return new SlotCheckDto(check.IsFree, check.HasBlockingIssue, check.Issues.Select(i => IssueMessages[i]).ToList());
    }

    // ───────────────────────── Compromissos ─────────────────────────

    public async Task<IReadOnlyList<AppointmentDto>> ListForLeadAsync(Guid leadId, CancellationToken ct)
    {
        await leads.GetVisibleAsync(leadId, ct);
        return (await AppointmentsQuery(a => a.LeadId == leadId).ToListAsync(ct)).OrderByDescending(a => a.StartsAt).Select(Mask).ToList();
    }

    public async Task<Guid> CreateAppointmentAsync(CreateAppointmentCommand cmd, CancellationToken ct)
    {
        LeadRef? lead = cmd.LeadId is { } leadId ? await leads.GetVisibleAsync(leadId, ct) : null;
        var agentId = await members.RequireUserAsync(Capabilities.CalendarManage, cmd.AgentUserId ?? lead?.OwnerUserId, ct);

        var schedule = await db.AgentSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == agentId, ct);
        var endsAt = cmd.EndsAt ?? cmd.StartsAt.AddMinutes(schedule?.VisitDurationMinutes ?? 60);
        var title = string.IsNullOrWhiteSpace(cmd.Title)
            ? (cmd.Kind == AppointmentKind.Visit && lead is not null ? $"Visita — {lead.ContactName}" : "Compromisso")
            : cmd.Title;

        var check = await CheckAsync(agentId, cmd.StartsAt, endsAt, cmd.Kind == AppointmentKind.Visit, null, ct);
        EnsureAcceptable(check, cmd.ConfirmWarnings);

        var appointment = new Appointment(actor.OrganizationId, agentId, lead?.Id, cmd.Kind, title, cmd.StartsAt, endsAt, cmd.Location, cmd.Notes,
            actor.Kind, actor.Kind == ActorKind.System ? null : actor.UserId);
        db.Appointments.Add(appointment);

        audit.Record("appointment.created", lead is null ? "appointment" : "lead", lead?.Id ?? appointment.Id, new
        {
            appointmentId = appointment.Id, agentUserId = agentId, kind = cmd.Kind.ToString(), startsAt = appointment.StartsAt, endsAt = appointment.EndsAt,
            overriddenWarnings = check.Warnings.Select(w => w.ToString()),
        });
        await SaveAsync(ct);
        return appointment.Id;
    }

    public async Task RescheduleAsync(Guid id, RescheduleAppointmentCommand cmd, CancellationToken ct)
    {
        var appointment = await LoadForManageAsync(id, ct);
        if (appointment.Version != cmd.ExpectedVersion)
            throw new ConcurrencyException("O compromisso foi alterado por outra pessoa. Recarregue e tente novamente.");

        var check = await CheckAsync(appointment.AgentUserId, cmd.StartsAt, cmd.EndsAt, appointment.Kind == AppointmentKind.Visit, appointment.Id, ct);
        EnsureAcceptable(check, cmd.ConfirmWarnings);

        var previous = new { appointment.StartsAt, appointment.EndsAt };
        appointment.Reschedule(cmd.Title, cmd.StartsAt, cmd.EndsAt, cmd.Location, cmd.Notes);
        audit.Record("appointment.rescheduled", appointment.LeadId is null ? "appointment" : "lead", appointment.LeadId ?? appointment.Id,
            new { appointmentId = appointment.Id, previous, startsAt = appointment.StartsAt, endsAt = appointment.EndsAt, overriddenWarnings = check.Warnings.Select(w => w.ToString()) });
        await SaveAsync(ct);
    }

    public async Task CancelAsync(Guid id, CancelAppointmentCommand cmd, CancellationToken ct)
    {
        var appointment = await LoadForManageAsync(id, ct);
        appointment.Cancel(cmd.Reason);
        audit.Record("appointment.cancelled", appointment.LeadId is null ? "appointment" : "lead", appointment.LeadId ?? appointment.Id,
            new { appointmentId = appointment.Id, reason = cmd.Reason });
        await SaveAsync(ct);
    }

    public async Task CompleteAsync(Guid id, CompleteAppointmentCommand cmd, CancellationToken ct)
    {
        var appointment = await LoadForManageAsync(id, ct);
        appointment.Complete(cmd.Attended);
        if (cmd.Attended && appointment.LeadId is { } leadId)
            await leads.RegisterContactAsync(leadId, appointment.StartsAt, ct);

        audit.Record(cmd.Attended ? "appointment.completed" : "appointment.no_show", appointment.LeadId is null ? "appointment" : "lead", appointment.LeadId ?? appointment.Id,
            new { appointmentId = appointment.Id });
        await SaveAsync(ct);
    }

    // ───────────────────────── helpers ─────────────────────────

    private async Task<SlotCheck> CheckAsync(Guid agentId, DateTimeOffset startsAt, DateTimeOffset endsAt, bool isVisit, Guid? ignoreAppointmentId, CancellationToken ct)
    {
        if (endsAt <= startsAt)
            throw new DomainException("appointment.range_invalid", "O fim deve ser posterior ao início.");

        var from = DateOnly.FromDateTime(startsAt.UtcDateTime).AddDays(-1);
        var to = DateOnly.FromDateTime(endsAt.UtcDateTime).AddDays(1);
        var (calculator, _) = await BuildCalculatorAsync(agentId, from, to, ignoreAppointmentId, ct);
        return calculator.Check(new TimeRange(startsAt.ToInstant(), endsAt.ToInstant()), isVisit);
    }

    private async Task<(AvailabilityCalculator Calculator, AgentSchedule? Schedule)> BuildCalculatorAsync(Guid agentId, DateOnly from, DateOnly to, Guid? ignoreAppointmentId, CancellationToken ct)
    {
        var calendar = await calendars.LoadAsync(from.AddDays(-1), to.AddDays(1), ct);
        var (start, end) = Bounds(calendar, from, to);
        var schedule = await db.AgentSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == agentId, ct);

        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.AgentUserId == agentId && a.Status == AppointmentStatus.Scheduled && a.Id != ignoreAppointmentId
                        && a.StartsAt < end.AddDays(1) && a.EndsAt > start.AddDays(-1))
            .Select(a => new { a.StartsAt, a.EndsAt, a.Kind })
            .ToListAsync(ct);
        var absences = await db.Absences.AsNoTracking()
            .Where(a => a.UserId == agentId && a.StartsAt < end.AddDays(1) && a.EndsAt > start.AddDays(-1))
            .Select(a => new { a.StartsAt, a.EndsAt })
            .ToListAsync(ct);

        var busy = appointments.Select(a => new BusyItem(new TimeRange(a.StartsAt.ToInstant(), a.EndsAt.ToInstant()), BusyKind.Appointment, a.Kind == AppointmentKind.Visit))
            .Concat(absences.Select(a => new BusyItem(new TimeRange(a.StartsAt.ToInstant(), a.EndsAt.ToInstant()), BusyKind.Absence, false)))
            .ToList();

        // Sem jornada cadastrada => nenhuma janela de trabalho (nunca se presume disponibilidade).
        var effective = schedule ?? new AgentSchedule(actor.OrganizationId, agentId);
        return (new AvailabilityCalculator(effective, calendar, busy, Now()), schedule);
    }

    private static void EnsureAcceptable(SlotCheck check, bool confirmWarnings)
    {
        if (check.HasBlockingIssue)
        {
            var blocking = check.Issues.Where(i => i is SlotIssue.Conflict or SlotIssue.Absence or SlotIssue.InPast).Select(i => IssueMessages[i]);
            throw new DomainException("appointment.unavailable", string.Join(" ", blocking));
        }
        if (check.Warnings.Count > 0 && !confirmWarnings)
            throw new DomainException("appointment.needs_confirmation", "Confirme para agendar mesmo assim: " + string.Join(" ", check.Warnings.Select(w => IssueMessages[w])));
    }

    private async Task<Appointment> LoadForManageAsync(Guid id, CancellationToken ct)
    {
        var scope = actor.Require(Capabilities.CalendarManage);
        var allowed = members.UserIds(scope);
        return await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && allowed.Contains(a.AgentUserId), ct)
               ?? throw new NotFoundException("Compromisso");
    }

    /// <summary>Filtro aplicado na ENTIDADE (antes da projeção) para ser traduzível em SQL.</summary>
    private IQueryable<AppointmentDto> AppointmentsQuery(System.Linq.Expressions.Expression<Func<Appointment, bool>> filter)
    {
        var scope = actor.Require(Capabilities.CalendarRead);
        var allowed = members.UserIds(scope);
        var source = db.Appointments.AsNoTracking().Where(filter).Where(a => allowed.Contains(a.AgentUserId)).Take(500);
        return from a in source
               join u in db.Users on a.AgentUserId equals u.Id
               join l in db.Leads on a.LeadId equals (Guid?)l.Id into ls
               from l in ls.DefaultIfEmpty()
               join c in db.Contacts on l.ContactId equals c.Id into cs
               from c in cs.DefaultIfEmpty()
               select new AppointmentDto(a.Id, a.AgentUserId, u.DisplayName, a.LeadId, c != null ? c.FullName : null, a.Kind, a.Title, a.StartsAt, a.EndsAt,
                   a.Location, a.Notes, a.Status, a.CancelReason, a.CreatedByKind.ToString(), a.Version);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            throw new ConcurrencyException("Este horário acabou de ser ocupado por outro compromisso do corretor.");
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("O compromisso foi alterado por outra pessoa. Recarregue e tente novamente.");
        }
    }

    private static (DateTimeOffset Start, DateTimeOffset End) Bounds(CalendarModel calendar, DateOnly from, DateOnly to) =>
        (from.ToLocalDate().AtStartOfDayInZone(calendar.Zone).ToInstant().ToDateTimeOffset(),
         to.ToLocalDate().PlusDays(1).AtStartOfDayInZone(calendar.Zone).ToInstant().ToDateTimeOffset());

    private Instant Now() => clock.GetUtcNow().ToInstant();

    private AppointmentDto Mask(AppointmentDto a) => mask.IsMasking
        ? a with { LeadName = mask.OptionalName(a.LeadName), Title = a.LeadName is null ? a.Title : a.Title.Replace(a.LeadName, mask.Name(a.LeadName), StringComparison.Ordinal), Notes = mask.FreeText(a.Notes), CancelReason = mask.FreeText(a.CancelReason) }
        : a;

    private static AgentScheduleDto ToDto(Guid userId, AgentSchedule? s) => s is null
        ? new AgentScheduleDto(userId, false, [], 60, 30, 120, null, false)
        : new AgentScheduleDto(userId, true, s.WorkBlocks, s.VisitDurationMinutes, s.BufferMinutes, s.MinNoticeMinutes, s.MaxVisitsPerDay, s.WorksOnHolidays);
}
