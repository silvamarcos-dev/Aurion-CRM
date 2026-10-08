using System.Net;
using System.Net.Http.Json;
using Loteadora.Crm.Tests.Infrastructure;

namespace Loteadora.Crm.Tests.Integration;

/// <summary>RBAC + escopo (own/team/org) aplicados no backend; recurso fora do escopo = 404 (anti-IDOR).</summary>
[Collection(ApiCollection.Name)]
public sealed class AuthorizationScopeTests(CrmApiFactory factory)
{
    [Fact]
    public async Task Agent_cannot_access_lead_owned_by_another_agent()
    {
        var norte1 = await factory.LoginAsync("corretor.norte1@a.test");
        var norte2 = await factory.LoginAsync("corretor.norte2@a.test");
        var leadId = await norte1.CreateLeadAsync("Lead do Norte 1");
        var lead = await norte1.GetLeadAsync(leadId);

        Assert.Equal(HttpStatusCode.NotFound, (await norte2.GetAsync(new Uri($"/api/v1/leads/{leadId}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await norte2.MoveAsync(leadId, factory.OrgA.StageIds[1], lead.Version)).StatusCode);
    }

    [Fact]
    public async Task Manager_sees_team_leads_but_not_other_team()
    {
        var norte1 = await factory.LoginAsync("corretor.norte1@a.test");
        var sul = await factory.LoginAsync("corretor.sul@a.test");
        var gestorNorte = await factory.LoginAsync("gestor.norte@a.test");

        var norteLead = await norte1.CreateLeadAsync("Lead time Norte");
        var sulLead = await sul.CreateLeadAsync("Lead time Sul");

        Assert.Equal(HttpStatusCode.OK, (await gestorNorte.GetAsync(new Uri($"/api/v1/leads/{norteLead}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await gestorNorte.GetAsync(new Uri($"/api/v1/leads/{sulLead}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Agent_cannot_create_lead_for_someone_else()
    {
        var norte1 = await factory.LoginAsync("corretor.norte1@a.test");
        var response = await norte1.PostAsJsonAsync("/api/v1/leads",
            new { fullName = "Tentativa", ownerUserId = factory.OrgA.UserIdsByEmail["corretor.sul@a.test"] }, TestHelpers.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_cannot_assign_lead_to_agent_of_other_team()
    {
        var norte1 = await factory.LoginAsync("corretor.norte1@a.test");
        var gestorNorte = await factory.LoginAsync("gestor.norte@a.test");
        var leadId = await norte1.CreateLeadAsync("Lead para reatribuir");

        var toSul = await gestorNorte.PostAsJsonAsync($"/api/v1/leads/{leadId}/assign",
            new { ownerUserId = factory.OrgA.UserIdsByEmail["corretor.sul@a.test"] }, TestHelpers.Json);
        var toNorte2 = await gestorNorte.PostAsJsonAsync($"/api/v1/leads/{leadId}/assign",
            new { ownerUserId = factory.OrgA.UserIdsByEmail["corretor.norte2@a.test"] }, TestHelpers.Json);

        Assert.Equal(HttpStatusCode.Forbidden, toSul.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, toNorte2.StatusCode);
    }

    [Fact]
    public async Task Agent_cannot_configure_pipeline()
    {
        var norte1 = await factory.LoginAsync("corretor.norte1@a.test");
        var response = await norte1.PostAsJsonAsync($"/api/v1/pipelines/{factory.OrgA.DefaultPipelineId}/stages",
            new { name = "Nova", kind = "Open" }, TestHelpers.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Requests_without_token_are_rejected()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri("/api/v1/leads", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri("/api/v1/board", UriKind.Relative))).StatusCode);
    }
}
