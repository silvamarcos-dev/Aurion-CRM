using Loteadora.Crm.Modules.Crm.Domain;
using Loteadora.Crm.Modules.Pipeline.Domain;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Security;

namespace Loteadora.Crm.Tests.Unit;

public sealed class LeadDomainTests
{
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static (SalesPipeline Pipeline, Lead Lead) NewLead()
    {
        var pipeline = new SalesPipeline(Org, "Funil", true);
        pipeline.AddStage("Novo", StageKind.Open);
        pipeline.AddStage("Qualificado", StageKind.Open);
        pipeline.AddStage("Venda", StageKind.Won);
        pipeline.AddStage("Perdido", StageKind.Lost);
        var lead = new Lead(Org, Guid.CreateVersion7(), Guid.CreateVersion7(), pipeline.Stages[0], "Site", null, Now);
        return (pipeline, lead);
    }

    [Fact]
    public void New_lead_raises_created_event()
    {
        var (_, lead) = NewLead();
        Assert.Single(lead.DomainEvents.OfType<LeadCreated>());
    }

    [Fact]
    public void Moving_updates_status_and_raises_event()
    {
        var (pipeline, lead) = NewLead();
        var transition = lead.MoveTo(pipeline.Stages[2], ActorKind.User, Guid.NewGuid(), null, Now.AddHours(1));

        Assert.Equal(LeadStatus.Won, lead.Status);
        Assert.Equal(pipeline.Stages[0].Id, transition.FromStageId);
        Assert.Single(lead.DomainEvents.OfType<LeadStageChanged>());
    }

    [Fact]
    public void Lost_requires_reason()
    {
        var (pipeline, lead) = NewLead();
        var ex = Assert.Throws<DomainException>(() => lead.MoveTo(pipeline.Stages[3], ActorKind.User, null, " ", Now));
        Assert.Equal("lead.lost_reason_required", ex.Code);
    }

    [Fact]
    public void Cannot_move_to_stage_of_other_pipeline_or_tenant()
    {
        var (_, lead) = NewLead();
        var otherPipeline = new SalesPipeline(Org, "Outro", false);
        var foreign = otherPipeline.AddStage("X", StageKind.Open);
        var otherTenant = new SalesPipeline(Guid.CreateVersion7(), "Outro tenant", true).AddStage("Y", StageKind.Open);

        Assert.Throws<DomainException>(() => lead.MoveTo(foreign, ActorKind.User, null, null, Now));
        Assert.Throws<DomainException>(() => lead.MoveTo(otherTenant, ActorKind.User, null, null, Now));
    }

    [Fact]
    public void Budget_range_must_be_consistent()
    {
        var (_, lead) = NewLead();
        Assert.Throws<DomainException>(() =>
            lead.UpdateDetails(null, null, null, 200_000_00, 100_000_00, PurchaseTimeframe.Unknown, null, null, null, null));
    }

    [Theory]
    [InlineData("(11) 98765-4321", "+5511987654321")]
    [InlineData("+55 11 98765-4321", "+5511987654321")]
    [InlineData("1133334444", "+551133334444")]
    public void Phone_is_normalized_to_e164(string raw, string expected) =>
        Assert.Equal(expected, PhoneNumber.NormalizeE164(raw));
}
