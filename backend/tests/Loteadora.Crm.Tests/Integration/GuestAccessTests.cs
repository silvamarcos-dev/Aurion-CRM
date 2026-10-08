using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Loteadora.Crm.Tests.Integration;

/// <summary>Modo visitante / desenvolvedor por link de acesso: somente leitura, dados mascarados, revogável.</summary>
[Collection(ApiCollection.Name)]
public sealed class GuestAccessTests(CrmApiFactory factory)
{
    private async Task<(Guid LinkId, string Token)> CreateLinkAsync(HttpClient admin, string role, string label = "Convidado teste")
    {
        var response = await admin.PostAsJsonAsync("/api/v1/access-links", new { label, role, expiresInDays = 7, note = (string?)null }, TestHelpers.Json);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("token").GetString()!);
    }

    private async Task<HttpClient> RedeemAsync(string token)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/access-link", new { token });
        response.EnsureSuccessStatusCode();
        var tokens = (await response.Content.ReadFromJsonAsync<TokenPair>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task Visitor_sees_masked_data_and_cannot_change_anything()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        var leadResponse = await admin.PostAsJsonAsync("/api/v1/leads", new { fullName = "Valentina Prado Ribeiro", phone = "11955554444", email = "valentina@exemplo.com" }, TestHelpers.Json);
        var leadId = (await leadResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var (_, token) = await CreateLinkAsync(admin, SystemRoles.Visitor);
        var visitor = await RedeemAsync(token);

        var me = await visitor.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.True(me.GetProperty("isGuest").GetBoolean());
        Assert.Equal("Visitante", me.GetProperty("role").GetString());

        var lead = await visitor.GetFromJsonAsync<JsonElement>($"/api/v1/leads/{leadId}");
        Assert.Equal("Valentina R.", lead.GetProperty("fullName").GetString());
        Assert.Equal("+55 11 •••••-4444", lead.GetProperty("phone").GetString());
        Assert.Equal("v•••@exemplo.com", lead.GetProperty("email").GetString());

        // Busca por telefone não pode servir para "descobrir" números mascarados.
        var byPhone = await visitor.GetFromJsonAsync<JsonElement>("/api/v1/leads?search=955554444");
        Assert.Equal(0, byPhone.GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.PostAsJsonAsync("/api/v1/leads", new { fullName = "Novo" }, TestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.PostAsJsonAsync($"/api/v1/leads/{leadId}/move", new { stageId = factory.OrgA.StageIds[1], expectedVersion = 0 }, TestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.PostAsJsonAsync("/api/v1/tasks", new { kind = "Other", title = "x", dueAt = DateTimeOffset.UtcNow }, TestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.GetAsync(new Uri("/api/v1/dev/system", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.GetAsync(new Uri("/api/v1/access-links", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Revoking_link_ends_access_immediately()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        var (linkId, token) = await CreateLinkAsync(admin, SystemRoles.Visitor);
        var visitor = await RedeemAsync(token);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(new Uri("/api/v1/me", UriKind.Relative))).StatusCode);

        (await admin.PostAsync(new Uri($"/api/v1/access-links/{linkId}/revoke", UriKind.Relative), null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(new Uri("/api/v1/me", UriKind.Relative))).StatusCode);
        var again = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/access-link", new { token });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task Expired_guest_membership_is_rejected()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        var (linkId, token) = await CreateLinkAsync(admin, SystemRoles.Visitor);
        var visitor = await RedeemAsync(token);

        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE iam.memberships SET expires_at = now() - interval '1 minute' WHERE user_id = (SELECT guest_user_id FROM iam.access_links WHERE id = {linkId})"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(new Uri("/api/v1/me", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Only_admin_creates_links_and_only_guest_roles_are_allowed()
    {
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var admin = await factory.LoginAsync("admin@a.test");
        var adminB = await factory.LoginAsync("admin@b.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("/api/v1/access-links", new { label = "x", role = "Visitante", expiresInDays = 1 }, TestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/access-links", new { label = "x", role = "Administrador", expiresInDays = 1 }, TestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/access-links", new { label = "x", role = "Visitante", expiresInDays = 90 }, TestHelpers.Json)).StatusCode);

        var (linkId, _) = await CreateLinkAsync(admin, SystemRoles.Visitor);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.PostAsync(new Uri($"/api/v1/access-links/{linkId}/revoke", UriKind.Relative), null)).StatusCode);
    }

    [Fact]
    public async Task Invalid_token_is_rejected_generically()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/access-link", new { token = "nao-existe" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Developer_sees_dev_tools_and_masked_audit()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        var (_, token) = await CreateLinkAsync(admin, SystemRoles.Developer, "Dev freelancer");
        var dev = await RedeemAsync(token);

        var system = await dev.GetFromJsonAsync<JsonElement>("/api/v1/dev/system");
        Assert.Equal("Testing", system.GetProperty("environment").GetString());
        Assert.Empty(system.GetProperty("pendingMigrations").EnumerateArray());

        var endpoints = await dev.GetFromJsonAsync<JsonElement>("/api/v1/dev/endpoints");
        Assert.Contains(endpoints.EnumerateArray(), e => e.GetProperty("route").GetString() == "/api/v1/leads/{id:guid}/move");

        Assert.Equal(HttpStatusCode.OK, (await dev.GetAsync(new Uri("/api/v1/dev/outbox", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dev.GetAsync(new Uri("/api/v1/dev/roles", UriKind.Relative))).StatusCode);

        // Motivo de perda (texto livre) é removido da auditoria para quem não tem pii.view.
        var leadId = await admin.CreateLeadAsync("Lead com motivo");
        var lead = await admin.GetLeadAsync(leadId);
        (await admin.MoveAsync(leadId, factory.OrgA.StageIds[^1], lead.Version, "Cliente Fulano CPF 123")).EnsureSuccessStatusCode();
        var audit = await dev.GetFromJsonAsync<JsonElement>("/api/v1/dev/audit?action=lead.stage_changed&limit=5");
        Assert.All(audit.EnumerateArray(), a => Assert.DoesNotContain("CPF", a.GetProperty("data").GetString(), StringComparison.Ordinal));
    }
}

[Collection(ApiCollection.Name)]
public sealed class SuggestionTests(CrmApiFactory factory)
{
    private static object Suggestion(string title) => new
    {
        kind = "Improvement",
        title,
        description = "Seria ótimo poder filtrar por campanha.",
        pagePath = "/leads?search=Fulano#topo",
        pageTitle = "Leads",
        elementDescriptor = "button: Novo lead",
        viewport = "1280x800",
    };

    [Fact]
    public async Task Author_sees_own_manager_sees_all_and_triages()
    {
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var other = await factory.LoginAsync("corretor.sul@a.test");
        var admin = await factory.LoginAsync("admin@a.test");

        var created = await agent.PostAsJsonAsync("/api/v1/suggestions", Suggestion("Filtro por campanha"), TestHelpers.Json);
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var detail = await agent.GetFromJsonAsync<JsonElement>($"/api/v1/suggestions/{id}");
        Assert.Equal("/leads", detail.GetProperty("pagePath").GetString()); // query string e fragmento removidos
        Assert.False(detail.GetProperty("canTriage").GetBoolean());

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri($"/api/v1/suggestions/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync($"/api/v1/suggestions/{id}/triage", new { status = "Planned" }, TestHelpers.Json)).StatusCode);

        (await admin.PostAsJsonAsync($"/api/v1/suggestions/{id}/triage", new { status = "Planned", priority = "High" }, TestHelpers.Json)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/suggestions/{id}/comments", new { body = "Boa ideia, entra na próxima fase." }, TestHelpers.Json)).EnsureSuccessStatusCode();
        (await agent.PostAsJsonAsync($"/api/v1/suggestions/{id}/comments", new { body = "Obrigado!" }, TestHelpers.Json)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/suggestions/{id}/comments", new { body = "x" }, TestHelpers.Json)).StatusCode);

        var after = await agent.GetFromJsonAsync<JsonElement>($"/api/v1/suggestions/{id}");
        Assert.Equal("Planned", after.GetProperty("status").GetString());
        Assert.Equal(2, after.GetProperty("comments").GetArrayLength());
    }

    [Fact]
    public async Task Suggestions_are_isolated_per_organization()
    {
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var adminB = await factory.LoginAsync("admin@b.test");
        var created = await agent.PostAsJsonAsync("/api/v1/suggestions", Suggestion("Isolada"), TestHelpers.Json);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync(new Uri($"/api/v1/suggestions/{id}", UriKind.Relative))).StatusCode);
        var listB = await adminB.GetFromJsonAsync<JsonElement>("/api/v1/suggestions");
        Assert.DoesNotContain(listB.EnumerateArray(), s => s.GetProperty("id").GetGuid() == id);
    }
}
