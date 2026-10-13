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

    /// <summary>Acesso externo somente leitura, com dados pessoais mascarados.</summary>
    public const string Visitor = "Visitante";

    /// <summary>Acesso externo para apoio técnico: leitura + painel do desenvolvedor + gestão de sugestões. Dados mascarados.</summary>
    public const string Developer = "Desenvolvedor";

    /// <summary>Papéis concedidos por link de acesso (usuários convidados, sem senha).</summary>
    public static readonly IReadOnlySet<string> GuestRoles = new HashSet<string> { Visitor, Developer };

    public static IReadOnlyDictionary<string, CapabilityScope> GrantsFor(string role) => role switch
    {
        Admin => All.ToDictionary(c => c, _ => CapabilityScope.Org),
        Manager => new Dictionary<string, CapabilityScope>
        {
            [LeadsRead] = CapabilityScope.Team, [LeadsCreate] = CapabilityScope.Team, [LeadsUpdate] = CapabilityScope.Team,
            [LeadsAssign] = CapabilityScope.Team, [PipelineMove] = CapabilityScope.Team, [AuditRead] = CapabilityScope.Team,
            [CalendarRead] = CapabilityScope.Team, [CalendarManage] = CapabilityScope.Team,
            [TasksRead] = CapabilityScope.Team, [TasksManage] = CapabilityScope.Team,
            [MacrosUse] = CapabilityScope.Org, [MacrosManage] = CapabilityScope.Org,
            [PiiView] = CapabilityScope.Org,
            [SuggestionsCreate] = CapabilityScope.Own, [SuggestionsRead] = CapabilityScope.Own,
            [ConversationsRead] = CapabilityScope.Team, [ConversationsReply] = CapabilityScope.Team, [ConversationsAssign] = CapabilityScope.Team,
        },
        Agent => new Dictionary<string, CapabilityScope>
        {
            [LeadsRead] = CapabilityScope.Own, [LeadsCreate] = CapabilityScope.Own, [LeadsUpdate] = CapabilityScope.Own,
            [PipelineMove] = CapabilityScope.Own,
            [CalendarRead] = CapabilityScope.Own, [CalendarManage] = CapabilityScope.Own,
            [TasksRead] = CapabilityScope.Own, [TasksManage] = CapabilityScope.Own,
            [MacrosUse] = CapabilityScope.Org, [MacrosManage] = CapabilityScope.Own,
            [PiiView] = CapabilityScope.Org,
            [SuggestionsCreate] = CapabilityScope.Own, [SuggestionsRead] = CapabilityScope.Own,
            [ConversationsRead] = CapabilityScope.Own, [ConversationsReply] = CapabilityScope.Own,
        },
        Visitor => new Dictionary<string, CapabilityScope>
        {
            [LeadsRead] = CapabilityScope.Org, [CalendarRead] = CapabilityScope.Org, [TasksRead] = CapabilityScope.Org,
            [MacrosUse] = CapabilityScope.Org,
            [SuggestionsCreate] = CapabilityScope.Own, [SuggestionsRead] = CapabilityScope.Own,
            [ConversationsRead] = CapabilityScope.Org,
        },
        Developer => new Dictionary<string, CapabilityScope>
        {
            [LeadsRead] = CapabilityScope.Org, [CalendarRead] = CapabilityScope.Org, [TasksRead] = CapabilityScope.Org,
            [MacrosUse] = CapabilityScope.Org, [AuditRead] = CapabilityScope.Org, [Capabilities.DevTools] = CapabilityScope.Org,
            [SuggestionsCreate] = CapabilityScope.Own, [SuggestionsRead] = CapabilityScope.Org, [SuggestionsManage] = CapabilityScope.Org,
            [ConversationsRead] = CapabilityScope.Org,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Papel de sistema desconhecido."),
    };

    public static IReadOnlyList<Role> CreateFor(Guid organizationId) =>
        new[] { Admin, Manager, Agent, Visitor, Developer }.Select(name =>
        {
            var role = new Role(organizationId, name, isSystem: true);
            foreach (var (capability, scope) in GrantsFor(name))
                role.Grant(capability, scope);
            return role;
        }).ToList();
}
