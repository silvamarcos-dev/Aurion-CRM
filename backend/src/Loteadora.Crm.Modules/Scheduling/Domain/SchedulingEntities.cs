using Loteadora.Crm.Modules.BusinessCalendar.Domain;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;

namespace Loteadora.Crm.Modules.Scheduling.Domain;

/// <summary>
/// Jornada do corretor e regras de visita. É a base da "disponibilidade REAL" consultada pelo Copilot:
/// sem jornada cadastrada, o corretor NÃO tem disponibilidade (nunca se presume horário).
/// Intervalos entre blocos (ex.: 12:00–13:00) são os intervalos de almoço/pausa.
/// </summary>
public sealed class AgentSchedule : Entity, ITenantOwned, IHasTimestamps
{
    private AgentSchedule() { }

    public AgentSchedule(Guid organizationId, Guid userId)
    {
        OrganizationId = organizationId;
        UserId = userId;
    }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public List<WeeklyInterval> WorkBlocks { get; private set; } = [];

    /// <summary>Duração padrão de uma visita.</summary>
    public int VisitDurationMinutes { get; private set; } = 60;

    /// <summary>Folga antes/depois de compromissos (deslocamento).</summary>
    public int BufferMinutes { get; private set; } = 30;

    /// <summary>Antecedência mínima para agendar.</summary>
    public int MinNoticeMinutes { get; private set; } = 120;

    public int? MaxVisitsPerDay { get; private set; }
    public bool WorksOnHolidays { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Update(IEnumerable<WeeklyInterval> workBlocks, int visitDurationMinutes, int bufferMinutes, int minNoticeMinutes, int? maxVisitsPerDay, bool worksOnHolidays)
    {
        if (visitDurationMinutes is < 15 or > 480)
            throw new DomainException("schedule.duration_invalid", "Duração da visita deve estar entre 15 e 480 minutos.");
        if (bufferMinutes is < 0 or > 240)
            throw new DomainException("schedule.buffer_invalid", "Intervalo entre compromissos deve estar entre 0 e 240 minutos.");
        if (minNoticeMinutes is < 0 or > 10_080)
            throw new DomainException("schedule.notice_invalid", "Antecedência mínima deve estar entre 0 e 7 dias.");
        if (maxVisitsPerDay is < 1 or > 50)
            throw new DomainException("schedule.max_invalid", "Limite diário de visitas inválido.");

        WorkBlocks = WeeklyInterval.Validate(workBlocks).ToList();
        VisitDurationMinutes = visitDurationMinutes;
        BufferMinutes = bufferMinutes;
        MinNoticeMinutes = minNoticeMinutes;
        MaxVisitsPerDay = maxVisitsPerDay;
        WorksOnHolidays = worksOnHolidays;
    }
}

public enum AbsenceKind
{
    Vacation = 1,
    DayOff = 2,
    Blocked = 3,
    Sick = 4,
    Other = 5,
}

/// <summary>Indisponibilidade do corretor (férias, folga, horário bloqueado…).</summary>
public sealed class Absence : Entity, ITenantOwned, IHasTimestamps
{
    private Absence() { }

    public Absence(Guid organizationId, Guid userId, DateTimeOffset startsAt, DateTimeOffset endsAt, AbsenceKind kind, string? note)
    {
        if (endsAt <= startsAt)
            throw new DomainException("absence.range_invalid", "O fim deve ser posterior ao início.");
        if (endsAt - startsAt > TimeSpan.FromDays(366))
            throw new DomainException("absence.range_too_long", "Período máximo de 1 ano.");

        OrganizationId = organizationId;
        UserId = userId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Kind = kind;
        Note = Guard.MaxLength(note?.Trim(), 200, "Observação");
    }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public AbsenceKind Kind { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum AppointmentKind
{
    Visit = 1,
    Meeting = 2,
    Call = 3,
    Personal = 4,
}

public enum AppointmentStatus
{
    Scheduled = 1,
    Completed = 2,
    Cancelled = 3,
    NoShow = 4,
}

/// <summary>
/// Compromisso na agenda do corretor. Sobreposição de compromissos AGENDADOS do mesmo corretor
/// é impedida pelo banco (EXCLUDE constraint) — proteção final contra double-booking.
/// </summary>
public sealed class Appointment : Entity, ITenantOwned, IHasTimestamps
{
    private Appointment() { }

    public Appointment(Guid organizationId, Guid agentUserId, Guid? leadId, AppointmentKind kind, string title, DateTimeOffset startsAt, DateTimeOffset endsAt,
        string? location, string? notes, ActorKind createdByKind, Guid? createdById)
    {
        if (kind == AppointmentKind.Visit && leadId is null)
            throw new DomainException("appointment.lead_required", "Visita precisa estar vinculada a um lead.");

        OrganizationId = organizationId;
        AgentUserId = agentUserId;
        LeadId = leadId;
        Kind = kind;
        CreatedByKind = createdByKind;
        CreatedById = createdById;
        SetDetails(title, startsAt, endsAt, location, notes);
        Raise(new AppointmentScheduled(organizationId, Id, agentUserId, leadId, kind, startsAt, endsAt));
    }

    public Guid OrganizationId { get; private set; }
    public Guid AgentUserId { get; private set; }
    public Guid? LeadId { get; private set; }
    public AppointmentKind Kind { get; private set; }
    public string Title { get; private set; } = default!;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string? Location { get; private set; }
    public string? Notes { get; private set; }
    public AppointmentStatus Status { get; private set; } = AppointmentStatus.Scheduled;
    public string? CancelReason { get; private set; }
    public ActorKind CreatedByKind { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; private set; }

    public void Reschedule(string title, DateTimeOffset startsAt, DateTimeOffset endsAt, string? location, string? notes)
    {
        EnsureScheduled();
        SetDetails(title, startsAt, endsAt, location, notes);
        Raise(new AppointmentRescheduled(OrganizationId, Id, AgentUserId, LeadId, startsAt, endsAt));
    }

    public void Cancel(string? reason)
    {
        EnsureScheduled();
        Status = AppointmentStatus.Cancelled;
        CancelReason = Guard.MaxLength(reason?.Trim(), 300, "Motivo");
        Raise(new AppointmentCancelled(OrganizationId, Id, AgentUserId, LeadId));
    }

    public void Complete(bool attended)
    {
        EnsureScheduled();
        Status = attended ? AppointmentStatus.Completed : AppointmentStatus.NoShow;
    }

    private void EnsureScheduled()
    {
        if (Status != AppointmentStatus.Scheduled)
            throw new DomainException("appointment.not_scheduled", "O compromisso não está mais agendado.");
    }

    private void SetDetails(string title, DateTimeOffset startsAt, DateTimeOffset endsAt, string? location, string? notes)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("appointment.title_required", "Informe um título.");
        if (endsAt <= startsAt)
            throw new DomainException("appointment.range_invalid", "O fim deve ser posterior ao início.");
        if (endsAt - startsAt > TimeSpan.FromHours(12))
            throw new DomainException("appointment.too_long", "Compromisso com duração máxima de 12 horas.");

        Title = Guard.MaxLength(title.Trim(), 160, "Título")!;
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        Location = Guard.MaxLength(location?.Trim(), 300, "Local");
        Notes = Guard.MaxLength(notes?.Trim(), 2000, "Observações");
    }
}

public sealed record AppointmentScheduled(Guid OrganizationId, Guid AppointmentId, Guid AgentUserId, Guid? LeadId, AppointmentKind Kind, DateTimeOffset StartsAt, DateTimeOffset EndsAt) : IDomainEvent;

public sealed record AppointmentRescheduled(Guid OrganizationId, Guid AppointmentId, Guid AgentUserId, Guid? LeadId, DateTimeOffset StartsAt, DateTimeOffset EndsAt) : IDomainEvent;

public sealed record AppointmentCancelled(Guid OrganizationId, Guid AppointmentId, Guid AgentUserId, Guid? LeadId) : IDomainEvent;
