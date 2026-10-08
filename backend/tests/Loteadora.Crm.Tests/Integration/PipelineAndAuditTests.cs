using System.Net;
using System.Net.Http.Json;
using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.Audit.Domain;
using Loteadora.Crm.Modules.Outbox;
using Loteadora.Crm.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Loteadora.Crm.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class PipelineAndAuditTests(CrmApiFactory factory)
{
    private static IQueryable<OutboxMessage> OutboxFor(Loteadora.Crm.Modules.Persistence.AppDbContext db, Guid leadId) =>
        db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM messaging.outbox_messages WHERE payload->>'leadId' = {leadId.ToString()}");

    [Fact]
    public async Task Moving_lead_records_transition_audit_and_outbox_event()
    {
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var leadId = await agent.CreateLeadAsync("Lead que avança");
        var lead = await agent.GetLeadAsync(leadId);
        var target = factory.OrgA.StageIds[3];

        var response = await agent.MoveAsync(leadId, target, lead.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var (transitions, outbox) = await factory.WithDbAsync(async db =>
        {
            var t = await db.LeadStageTransitions.IgnoreQueryFilters().CountAsync(x => x.LeadId == leadId && x.ToStageId == target);
            var o = await OutboxFor(db, leadId).CountAsync(x => x.Type.EndsWith("LeadStageChanged"));
            return (t, o);
        });
        Assert.Equal(1, transitions);
        Assert.Equal(1, outbox);

        var history = await agent.GetFromJsonAsync<List<System.Text.Json.JsonElement>>($"/api/v1/leads/{leadId}/history", TestHelpers.Json);
        Assert.Contains(history!, h => h.GetProperty("action").GetString() == AuditActions.LeadStageChanged);
    }

    [Fact]
    public async Task Stale_version_is_rejected_with_conflict()
    {
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var leadId = await agent.CreateLeadAsync("Lead concorrente");
        var lead = await agent.GetLeadAsync(leadId);

        Assert.Equal(HttpStatusCode.OK, (await agent.MoveAsync(leadId, factory.OrgA.StageIds[1], lead.Version)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await agent.MoveAsync(leadId, factory.OrgA.StageIds[2], lead.Version)).StatusCode);
    }

    [Fact]
    public async Task Lost_stage_requires_reason()
    {
        var agent = await factory.LoginAsync("corretor.norte1@a.test");
        var leadId = await agent.CreateLeadAsync("Lead perdido");
        var lead = await agent.GetLeadAsync(leadId);
        var lost = factory.OrgA.StageIds[^1];

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await agent.MoveAsync(leadId, lost, lead.Version)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.MoveAsync(leadId, lost, lead.Version, "Sem orçamento")).StatusCode);
        Assert.Equal("Lost", (await agent.GetLeadAsync(leadId)).Status.ToString());
    }

    [Fact]
    public async Task Audit_chain_is_valid_and_rows_are_immutable()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        await admin.CreateLeadAsync("Lead auditado");

        var events = await factory.WithDbAsync(db => db.AuditEvents.IgnoreQueryFilters()
            .Where(a => a.OrganizationId == factory.OrgA.OrganizationId)
            .OrderBy(a => a.Sequence).ToListAsync());

        Assert.NotEmpty(events);
        var previous = AuditEvent.GenesisHash;
        for (var i = 0; i < events.Count; i++)
        {
            Assert.Equal(i + 1, events[i].Sequence);
            Assert.Equal(previous, events[i].PreviousHash);
            Assert.Equal(events[i].ComputeHash(), events[i].Hash);
            previous = events[i].Hash;
        }

        await Assert.ThrowsAnyAsync<Exception>(() => factory.WithDbAsync(db =>
            db.Database.ExecuteSqlRawAsync("UPDATE audit.audit_events SET action = 'adulterado'")));
        await Assert.ThrowsAnyAsync<Exception>(() => factory.WithDbAsync(db =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM audit.audit_events")));
    }

    [Fact]
    public async Task Outbox_dispatcher_marks_messages_processed()
    {
        var admin = await factory.LoginAsync("admin@a.test");
        var leadId = await admin.CreateLeadAsync("Lead outbox");

        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(factory.Services);
        while (await dispatcher.ProcessBatchAsync(CancellationToken.None) > 0)
        {
        }

        var pending = await factory.WithDbAsync(db => OutboxFor(db, leadId).CountAsync(m => m.ProcessedAt == null));
        Assert.Equal(0, pending);
    }
}
