using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Loteadora.Crm.Tests.Infrastructure;

namespace Loteadora.Crm.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class SchedulingTests(CrmApiFactory factory)
{
    private static readonly object FullWeekSchedule = new
    {
        workBlocks = Enumerable.Range(1, 7).Select(d => new { isoWeekday = d, start = "00:00:00", end = "23:59:00" }),
        visitDurationMinutes = 60,
        bufferMinutes = 0,
        minNoticeMinutes = 0,
        maxVisitsPerDay = (int?)null,
        worksOnHolidays = true,
    };

    private static DateTimeOffset NextSlot(int daysAhead, int hour) =>
        new DateTimeOffset(DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(hour), TimeSpan.Zero);

    [Fact]
    public async Task Concurrent_bookings_for_same_agent_never_overlap()
    {
        var agent = await factory.LoginAsync("corretor.norte2@a.test");
        (await agent.PutAsJsonAsync("/api/v1/agenda/schedule", FullWeekSchedule, TestHelpers.Json)).EnsureSuccessStatusCode();
        var leadId = await agent.CreateLeadAsync("Lead visita concorrente");
        var start = NextSlot(20, 15);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            agent.PostAsJsonAsync("/api/v1/appointments", new { leadId, kind = "Visit", startsAt = start, endsAt = start.AddHours(1), confirmWarnings = true }, TestHelpers.Json)));

        Assert.Equal(1, attempts.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(attempts.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.True(r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity, r.StatusCode.ToString()));
    }

    [Fact]
    public async Task Agenda_and_lead_appointments_list_visits_with_lead_names()
    {
        var agent = await factory.LoginAsync("corretor.norte2@a.test");
        (await agent.PutAsJsonAsync("/api/v1/agenda/schedule", FullWeekSchedule, TestHelpers.Json)).EnsureSuccessStatusCode();
        var leadId = await agent.CreateLeadAsync("Lead listagem agenda");
        var start = NextSlot(22, 11);
        (await agent.PostAsJsonAsync("/api/v1/appointments", new { leadId, kind = "Visit", startsAt = start, endsAt = start.AddHours(1), confirmWarnings = true }, TestHelpers.Json)).EnsureSuccessStatusCode();

        var day = DateOnly.FromDateTime(start.UtcDateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var agenda = await agent.GetFromJsonAsync<JsonElement>($"/api/v1/agenda?from={day}&to={day}");
        Assert.Contains(agenda.GetProperty("appointments").EnumerateArray(), a => a.GetProperty("leadName").GetString() == "Lead listagem agenda");

        var forLead = await agent.GetFromJsonAsync<JsonElement>($"/api/v1/leads/{leadId}/appointments");
        Assert.Equal(1, forLead.GetArrayLength());
    }

    [Fact]
    public async Task Cancelled_appointment_frees_the_slot()
    {
        var agent = await factory.LoginAsync("corretor.norte2@a.test");
        (await agent.PutAsJsonAsync("/api/v1/agenda/schedule", FullWeekSchedule, TestHelpers.Json)).EnsureSuccessStatusCode();
        var leadId = await agent.CreateLeadAsync("Lead cancelamento");
        var start = NextSlot(21, 10);
        var body = new { leadId, kind = "Visit", startsAt = start, endsAt = start.AddHours(1), confirmWarnings = true };

        var first = await agent.PostAsJsonAsync("/api/v1/appointments", body, TestHelpers.Json);
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await agent.PostAsJsonAsync("/api/v1/appointments", body, TestHelpers.Json)).StatusCode);

        (await agent.PostAsJsonAsync($"/api/v1/appointments/{id}/cancel", new { reason = "Cliente desmarcou" }, TestHelpers.Json)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync("/api/v1/appointments", body, TestHelpers.Json)).StatusCode);
    }

    [Fact]
    public async Task Agent_without_schedule_has_no_availability_and_needs_confirmation()
    {
        var agent = await factory.LoginAsync("corretor.sul@a.test");
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var availability = await agent.GetFromJsonAsync<JsonElement>($"/api/v1/agenda/availability?from={from}&to={to}");
        Assert.False(availability.GetProperty("isScheduleConfigured").GetBoolean());
        Assert.Equal(0, availability.GetProperty("slots").GetArrayLength());

        var leadId = await agent.CreateLeadAsync("Lead sem jornada");
        var start = NextSlot(5, 14);
        var response = await agent.PostAsJsonAsync("/api/v1/appointments", new { leadId, kind = "Visit", startsAt = start, confirmWarnings = false }, TestHelpers.Json);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("appointment.needs_confirmation", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Agent_cannot_read_or_book_other_agents_agenda()
    {
        var norte1 = await factory.LoginAsync("corretor.norte1@a.test");
        var sulId = factory.OrgA.UserIdsByEmail["corretor.sul@a.test"];
        var day = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(HttpStatusCode.NotFound, (await norte1.GetAsync(new Uri($"/api/v1/agenda?userId={sulId}&from={day}&to={day}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await norte1.PutAsJsonAsync($"/api/v1/agenda/schedule?userId={sulId}", FullWeekSchedule, TestHelpers.Json)).StatusCode);

        var start = NextSlot(9, 13);
        var booking = await norte1.PostAsJsonAsync("/api/v1/appointments",
            new { agentUserId = sulId, kind = "Meeting", title = "Reunião", startsAt = start, endsAt = start.AddHours(1), confirmWarnings = true }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.NotFound, booking.StatusCode);
    }

    [Fact]
    public async Task Other_org_cannot_see_appointments_of_lead()
    {
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var adminB = await factory.LoginAsync("admin@b.test");
        var leadId = await agent.CreateLeadAsync("Lead agenda isolada");

        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync(new Uri($"/api/v1/leads/{leadId}/appointments", UriKind.Relative))).StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public sealed class TaskAndMacroTests(CrmApiFactory factory)
{
    [Fact]
    public async Task Follow_up_tasks_drive_lead_next_follow_up()
    {
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var leadId = await agent.CreateLeadAsync("Lead follow-up");
        var early = DateTimeOffset.UtcNow.AddDays(1);
        var late = DateTimeOffset.UtcNow.AddDays(3);

        var t1 = await CreateTaskAsync(agent, leadId, late);
        var t2 = await CreateTaskAsync(agent, leadId, early);
        Assert.Equal(early.ToUnixTimeSeconds(), (await agent.GetLeadAsync(leadId)).NextFollowUpAt!.Value.ToUnixTimeSeconds());

        (await agent.PostAsync(new Uri($"/api/v1/tasks/{t2}/complete", UriKind.Relative), null)).EnsureSuccessStatusCode();
        var afterFirst = await agent.GetLeadAsync(leadId);
        Assert.Equal(late.ToUnixTimeSeconds(), afterFirst.NextFollowUpAt!.Value.ToUnixTimeSeconds());
        Assert.NotNull(afterFirst.LastContactAt);

        var listed = await agent.GetFromJsonAsync<JsonElement>($"/api/v1/tasks?leadId={leadId}");
        Assert.All(listed.EnumerateArray(), t => Assert.Equal("Lead follow-up", t.GetProperty("leadName").GetString()));
        var mine = await agent.GetFromJsonAsync<JsonElement>("/api/v1/tasks?status=Open&due=Upcoming");
        Assert.Contains(mine.EnumerateArray(), t => t.GetProperty("id").GetGuid() == t1);

        (await agent.PostAsync(new Uri($"/api/v1/tasks/{t1}/cancel", UriKind.Relative), null)).EnsureSuccessStatusCode();
        Assert.Null((await agent.GetLeadAsync(leadId)).NextFollowUpAt);
    }

    [Fact]
    public async Task Agent_cannot_assign_task_to_other_agent_or_touch_their_tasks()
    {
        var norte1 = await factory.LoginAsync("corretor.norte1@a.test");
        var norte2 = await factory.LoginAsync("corretor.norte2@a.test");
        var leadId = await norte1.CreateLeadAsync("Lead tarefa privada");
        var taskId = await CreateTaskAsync(norte1, leadId, DateTimeOffset.UtcNow.AddDays(1));

        var assign = await norte1.PostAsJsonAsync("/api/v1/tasks",
            new { assigneeUserId = factory.OrgA.UserIdsByEmail["corretor.norte2@a.test"], kind = "Other", title = "X", dueAt = DateTimeOffset.UtcNow }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.NotFound, assign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await norte2.PostAsync(new Uri($"/api/v1/tasks/{taskId}/complete", UriKind.Relative), null)).StatusCode);
    }

    [Fact]
    public async Task Agent_creates_personal_macro_but_not_shared_and_render_respects_lead_scope()
    {
        var norte1 = await factory.LoginAsync("corretor.norte1@a.test");
        var sul = await factory.LoginAsync("corretor.sul@a.test");

        var shared = await norte1.PostAsJsonAsync("/api/v1/macros", new { shortcut = "/oficial", title = "Oficial", body = "Texto", shared = true }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.Forbidden, shared.StatusCode);

        var personal = await norte1.PostAsJsonAsync("/api/v1/macros", new { shortcut = "/meu-oi", title = "Meu oi", body = "Oi {{lead.first_name}}, aqui é {{agent.first_name}}!", shared = false }, TestHelpers.Json);
        personal.EnsureSuccessStatusCode();
        var macroId = (await personal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var ownLead = await norte1.CreateLeadAsync("Joana Teste");
        var rendered = await (await norte1.PostAsJsonAsync($"/api/v1/macros/{macroId}/render", new { leadId = ownLead }, TestHelpers.Json)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Oi Joana, aqui é Corretor!", rendered.GetProperty("text").GetString());

        // Lead de outro corretor: fora do escopo => 404 (a macro não vaza dados).
        var otherLead = await sul.CreateLeadAsync("Lead do Sul");
        Assert.Equal(HttpStatusCode.NotFound, (await norte1.PostAsJsonAsync($"/api/v1/macros/{macroId}/render", new { leadId = otherLead }, TestHelpers.Json)).StatusCode);

        // Macro pessoal não aparece para outro corretor.
        var sulMacros = await sul.GetFromJsonAsync<JsonElement>("/api/v1/macros");
        Assert.DoesNotContain(sulMacros.EnumerateArray(), m => m.GetProperty("shortcut").GetString() == "/meu-oi");
    }

    [Fact]
    public async Task Holiday_import_creates_unconfirmed_entries_and_requires_admin()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        var agent = await factory.LoginAsync("corretor.norte1@a.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsync(new Uri("/api/v1/calendar/holidays/import?year=2031", UriKind.Relative), null)).StatusCode);

        var result = await (await admin.PostAsync(new Uri("/api/v1/calendar/holidays/import?year=2031", UriKind.Relative), null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, result.GetProperty("imported").GetInt32());

        var holidays = await admin.GetFromJsonAsync<JsonElement>("/api/v1/calendar/holidays?year=2031");
        Assert.All(holidays.EnumerateArray(), h => Assert.False(h.GetProperty("isConfirmed").GetBoolean()));

        // Reimportar não duplica.
        var again = await (await admin.PostAsync(new Uri("/api/v1/calendar/holidays/import?year=2031", UriKind.Relative), null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, again.GetProperty("imported").GetInt32());
    }

    private static async Task<Guid> CreateTaskAsync(HttpClient client, Guid leadId, DateTimeOffset dueAt)
    {
        var response = await client.PostAsJsonAsync("/api/v1/tasks", new { leadId, kind = "FollowUp", title = "Retornar", dueAt }, TestHelpers.Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
