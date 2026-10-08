using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.SharedKernel.Security;
using static Loteadora.Crm.SharedKernel.Security.Capabilities;

namespace Loteadora.Crm.Modules.Iam.Application;

/// <summary>
/// Papéis padrão criados para cada nova organização (privacy by default: menor privilégio).
/// A organização pode criar papéis próprios a partir do catálogo de capabilities.
/// </summary>
public static class SystemRoles
{
    public const string Admin = "Administrador";
    public const string Manager = "Gestor";
    public const string Agent = "Corretor";

    public static IReadOnlyList<Role> CreateFor(Guid organizationId)
    {
        var admin = new Role(organizationId, Admin, isSystem: true);
        foreach (var capability in All)
            admin.Grant(capability, CapabilityScope.Org);

        var manager = new Role(organizationId, Manager, isSystem: true);
        manager.Grant(LeadsRead, CapabilityScope.Team);
        manager.Grant(LeadsCreate, CapabilityScope.Team);
        manager.Grant(LeadsUpdate, CapabilityScope.Team);
        manager.Grant(LeadsAssign, CapabilityScope.Team);
        manager.Grant(PipelineMove, CapabilityScope.Team);
        manager.Grant(AuditRead, CapabilityScope.Team);

        var agent = new Role(organizationId, Agent, isSystem: true);
        agent.Grant(LeadsRead, CapabilityScope.Own);
        agent.Grant(LeadsCreate, CapabilityScope.Own);
        agent.Grant(LeadsUpdate, CapabilityScope.Own);
        agent.Grant(PipelineMove, CapabilityScope.Own);

        return [admin, manager, agent];
    }
}
