using Loteadora.Crm.Modules.BusinessCalendar.Application;
using Loteadora.Crm.Modules.Macros.Application;
using Loteadora.Crm.Modules.Scheduling.Application;
using Loteadora.Crm.Modules.Tasks.Application;
using TaskStatus = Loteadora.Crm.Modules.Tasks.Domain.TaskStatus;

namespace Loteadora.Crm.Api.Endpoints;

public static class CalendarEndpoints
{
    public static void MapCalendarEndpoints(this RouteGroupBuilder api)
    {
        var calendar = api.MapGroup("/calendar").WithTags("Calendário comercial");

        calendar.MapGet("/status", (DateTimeOffset? at, BusinessCalendarService service, CancellationToken ct) => service.GetStatusAsync(at, ct));

        calendar.MapGet("/settings", (BusinessCalendarService service, CancellationToken ct) => service.GetSettingsAsync(ct));
        calendar.MapPut("/settings", async (CalendarSettingsDto dto, BusinessCalendarService service, CancellationToken ct) =>
        {
            await service.UpdateSettingsAsync(dto, ct);
            return Results.NoContent();
        });

        calendar.MapGet("/holidays", (int? year, BusinessCalendarService service, TimeProvider clock, CancellationToken ct) =>
            service.ListHolidaysAsync(year ?? clock.GetUtcNow().Year, ct));
        calendar.MapPost("/holidays", async (CreateHolidayCommand cmd, BusinessCalendarService service, CancellationToken ct) =>
            Results.Ok(new CreatedResponse(await service.CreateHolidayAsync(cmd, ct))));
        calendar.MapPost("/holidays/import", (int year, BusinessCalendarService service, CancellationToken ct) =>
            service.ImportNationalHolidaysAsync(year, ct));
        calendar.MapPost("/holidays/{id:guid}/confirm", async (Guid id, BusinessCalendarService service, CancellationToken ct) =>
        {
            await service.ConfirmHolidayAsync(id, ct);
            return Results.NoContent();
        });
        calendar.MapDelete("/holidays/{id:guid}", async (Guid id, BusinessCalendarService service, CancellationToken ct) =>
        {
            await service.DeleteHolidayAsync(id, ct);
            return Results.NoContent();
        });

        calendar.MapGet("/exceptions", (int? year, BusinessCalendarService service, TimeProvider clock, CancellationToken ct) =>
            service.ListExceptionsAsync(year ?? clock.GetUtcNow().Year, ct));
        calendar.MapPost("/exceptions", async (CreateCalendarExceptionCommand cmd, BusinessCalendarService service, CancellationToken ct) =>
            Results.Ok(new CreatedResponse(await service.CreateExceptionAsync(cmd, ct))));
        calendar.MapDelete("/exceptions/{id:guid}", async (Guid id, BusinessCalendarService service, CancellationToken ct) =>
        {
            await service.DeleteExceptionAsync(id, ct);
            return Results.NoContent();
        });
    }
}

public static class SchedulingEndpoints
{
    public static void MapSchedulingEndpoints(this RouteGroupBuilder api)
    {
        var agenda = api.MapGroup("/agenda").WithTags("Agenda");

        agenda.MapGet("/", (Guid? userId, DateOnly from, DateOnly to, SchedulingService service, CancellationToken ct) =>
            service.GetAgendaAsync(userId, from, to, ct));
        agenda.MapGet("/availability", (Guid? userId, DateOnly from, DateOnly to, int? durationMinutes, SchedulingService service, CancellationToken ct) =>
            service.GetAvailabilityAsync(userId, from, to, durationMinutes, ct));
        agenda.MapGet("/check", (Guid? userId, DateTimeOffset startsAt, DateTimeOffset endsAt, bool? isVisit, SchedulingService service, CancellationToken ct) =>
            service.CheckSlotAsync(userId, startsAt, endsAt, isVisit ?? true, ct));

        agenda.MapGet("/schedule", (Guid? userId, SchedulingService service, CancellationToken ct) => service.GetScheduleAsync(userId, ct));
        agenda.MapPut("/schedule", async (Guid? userId, UpdateAgentScheduleCommand cmd, SchedulingService service, CancellationToken ct) =>
        {
            await service.UpdateScheduleAsync(userId, cmd, ct);
            return Results.NoContent();
        });

        agenda.MapPost("/absences", async (CreateAbsenceCommand cmd, SchedulingService service, CancellationToken ct) =>
            Results.Ok(new CreatedResponse(await service.CreateAbsenceAsync(cmd, ct))));
        agenda.MapDelete("/absences/{id:guid}", async (Guid id, SchedulingService service, CancellationToken ct) =>
        {
            await service.DeleteAbsenceAsync(id, ct);
            return Results.NoContent();
        });

        var appointments = api.MapGroup("/appointments").WithTags("Agenda");
        appointments.MapPost("/", async (CreateAppointmentCommand cmd, SchedulingService service, CancellationToken ct) =>
            Results.Ok(new CreatedResponse(await service.CreateAppointmentAsync(cmd, ct))));
        appointments.MapPut("/{id:guid}", async (Guid id, RescheduleAppointmentCommand cmd, SchedulingService service, CancellationToken ct) =>
        {
            await service.RescheduleAsync(id, cmd, ct);
            return Results.NoContent();
        });
        appointments.MapPost("/{id:guid}/cancel", async (Guid id, CancelAppointmentCommand cmd, SchedulingService service, CancellationToken ct) =>
        {
            await service.CancelAsync(id, cmd, ct);
            return Results.NoContent();
        });
        appointments.MapPost("/{id:guid}/complete", async (Guid id, CompleteAppointmentCommand cmd, SchedulingService service, CancellationToken ct) =>
        {
            await service.CompleteAsync(id, cmd, ct);
            return Results.NoContent();
        });

        api.MapGet("/leads/{id:guid}/appointments", (Guid id, SchedulingService service, CancellationToken ct) => service.ListForLeadAsync(id, ct)).WithTags("Agenda");
    }
}

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this RouteGroupBuilder api)
    {
        var tasks = api.MapGroup("/tasks").WithTags("Tarefas");

        tasks.MapGet("/", (Guid? assigneeUserId, Guid? leadId, TaskStatus? status, TaskDueFilter? due, int? limit, TaskService service, CancellationToken ct) =>
            service.ListAsync(new TaskListQuery(assigneeUserId, leadId, status, due ?? TaskDueFilter.All, limit ?? 200), ct));
        tasks.MapPost("/", async (CreateTaskCommand cmd, TaskService service, CancellationToken ct) =>
            Results.Ok(new CreatedResponse(await service.CreateAsync(cmd, ct))));
        tasks.MapPut("/{id:guid}", async (Guid id, UpdateTaskCommand cmd, TaskService service, CancellationToken ct) =>
        {
            await service.UpdateAsync(id, cmd, ct);
            return Results.NoContent();
        });
        tasks.MapPost("/{id:guid}/complete", async (Guid id, TaskService service, CancellationToken ct) =>
        {
            await service.CompleteAsync(id, ct);
            return Results.NoContent();
        });
        tasks.MapPost("/{id:guid}/reopen", async (Guid id, TaskService service, CancellationToken ct) =>
        {
            await service.ReopenAsync(id, ct);
            return Results.NoContent();
        });
        tasks.MapPost("/{id:guid}/cancel", async (Guid id, TaskService service, CancellationToken ct) =>
        {
            await service.CancelAsync(id, ct);
            return Results.NoContent();
        });
    }
}

public static class MacroEndpoints
{
    public static void MapMacroEndpoints(this RouteGroupBuilder api)
    {
        var macros = api.MapGroup("/macros").WithTags("Macros");

        macros.MapGet("/", (bool? includeInactive, MacroService service, CancellationToken ct) => service.ListAsync(includeInactive ?? false, ct));
        macros.MapGet("/variables", () => MacroService.AvailableVariables);
        macros.MapPost("/", async (SaveMacroCommand cmd, MacroService service, CancellationToken ct) =>
            Results.Ok(new CreatedResponse(await service.CreateAsync(cmd, ct))));
        macros.MapPut("/{id:guid}", async (Guid id, SaveMacroCommand cmd, MacroService service, CancellationToken ct) =>
        {
            await service.UpdateAsync(id, cmd, ct);
            return Results.NoContent();
        });
        macros.MapDelete("/{id:guid}", async (Guid id, MacroService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });
        macros.MapPost("/{id:guid}/render", (Guid id, RenderMacroCommand cmd, MacroService service, CancellationToken ct) => service.RenderAsync(id, cmd, ct));
    }
}
