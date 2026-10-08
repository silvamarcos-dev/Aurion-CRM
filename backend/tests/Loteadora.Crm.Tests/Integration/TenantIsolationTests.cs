using System.Net;
using System.Net.Http.Json;
using Loteadora.Crm.Tests.Infrastructure;

namespace Loteadora.Crm.Tests.Integration;

/// <summary>Requisito crítico (LGPD 23.17): uma organização jamais acessa dados de outra.</summary>
[Collection(ApiCollection.Name)]
public sealed class TenantIsolationTests(CrmApiFactory factory)
{
    [Fact]
    public async Task Admin_of_other_org_cannot_read_update_move_assign_or_see_history_of_lead()
    {
        var adminA = await factory.LoginAsync("admin@a.test");
        var adminB = await factory.LoginAsync("admin@b.test");
        var leadId = await adminA.CreateLeadAsync("Titular da Org A");
        var lead = await adminA.GetLeadAsync(leadId);

        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync(new Uri($"/api/v1/leads/{leadId}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync(new Uri($"/api/v1/leads/{leadId}/history", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.MoveAsync(leadId, factory.OrgB.StageIds[1], lead.Version)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.MoveAsync(leadId, factory.OrgA.StageIds[1], lead.Version)).StatusCode);

        var update = await adminB.PutAsJsonAsync($"/api/v1/leads/{leadId}",
            new { fullName = "Invasor", timeframe = "Unknown", expectedVersion = lead.Version }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var assign = await adminB.PostAsJsonAsync($"/api/v1/leads/{leadId}/assign",
            new { ownerUserId = factory.OrgB.UserIdsByEmail["admin@b.test"] }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.NotFound, assign.StatusCode);

        // O lead permanece intacto.
        var after = await adminA.GetLeadAsync(leadId);
        Assert.Equal("Titular da Org A", after.FullName);
        Assert.Equal(lead.StageId, after.StageId);
    }

    [Fact]
    public async Task Listing_and_board_never_include_other_org_leads()
    {
        var adminA = await factory.LoginAsync("admin@a.test");
        var adminB = await factory.LoginAsync("admin@b.test");
        await adminA.CreateLeadAsync("Lead exclusivo A");

        var listB = await adminB.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/leads?search=exclusivo", TestHelpers.Json);
        Assert.Equal(0, listB.GetProperty("total").GetInt32());

        var boardB = await adminB.GetBoardAsync();
        Assert.All(boardB.Stages.SelectMany(s => s.Leads), l => Assert.DoesNotContain("exclusivo", l.ContactName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cannot_assign_lead_to_user_of_another_org()
    {
        var adminA = await factory.LoginAsync("admin@a.test");
        var leadId = await adminA.CreateLeadAsync("Lead para atribuição");

        var response = await adminA.PostAsJsonAsync($"/api/v1/leads/{leadId}/assign",
            new { ownerUserId = factory.OrgB.UserIdsByEmail["admin@b.test"] }, TestHelpers.Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cannot_move_lead_to_stage_of_another_org()
    {
        var adminA = await factory.LoginAsync("admin@a.test");
        var leadId = await adminA.CreateLeadAsync("Lead etapa estrangeira");
        var lead = await adminA.GetLeadAsync(leadId);

        var response = await adminA.MoveAsync(leadId, factory.OrgB.StageIds[2], lead.Version);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Persistence_layer_fails_closed_without_tenant()
    {
        var count = await factory.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Leads));
        Assert.Equal(0, count);
    }
}
