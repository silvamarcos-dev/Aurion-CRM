namespace Loteadora.Crm.SharedKernel.Security;

/// <summary>
/// Escopo de uma capability. Ordem crescente de abrangência:
/// Own (somente recursos próprios) &lt; Team (recursos do time) &lt; Org (toda a organização).
/// </summary>
public enum CapabilityScope
{
    Own = 1,
    Team = 2,
    Org = 3,
}

/// <summary>
/// Catálogo de capabilities. Papéis são apenas pacotes destas capabilities, editáveis por organização.
/// A autorização é SEMPRE verificada no backend.
/// </summary>
public static class Capabilities
{
    public const string LeadsRead = "leads.read";
    public const string LeadsCreate = "leads.create";
    public const string LeadsUpdate = "leads.update";
    public const string LeadsAssign = "leads.assign";
    public const string LeadsDelete = "leads.delete";
    public const string LeadsExport = "leads.export";

    public const string PipelineMove = "pipeline.move";
    public const string PipelineConfigure = "pipeline.configure";

    public const string AuditRead = "audit.read";

    public const string CalendarRead = "calendar.read";
    public const string CalendarManage = "calendar.manage";

    public const string TasksRead = "tasks.read";
    public const string TasksManage = "tasks.manage";

    public const string MacrosUse = "macros.use";
    public const string MacrosManage = "macros.manage";

    /// <summary>Ver dados pessoais completos (telefone, e-mail, nome completo). Sem ela, a API devolve dados mascarados.</summary>
    public const string PiiView = "pii.view";

    /// <summary>Painel do desenvolvedor (ambiente, saúde, fila de eventos, endpoints). Somente leitura.</summary>
    public const string DevTools = "dev.tools";

    public const string SuggestionsCreate = "suggestions.create";
    public const string SuggestionsRead = "suggestions.read";
    public const string SuggestionsManage = "suggestions.manage";

    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        LeadsRead, LeadsCreate, LeadsUpdate, LeadsAssign, LeadsDelete, LeadsExport,
        PipelineMove, PipelineConfigure,
        AuditRead,
        CalendarRead, CalendarManage,
        TasksRead, TasksManage,
        MacrosUse, MacrosManage,
        PiiView, DevTools,
        SuggestionsCreate, SuggestionsRead, SuggestionsManage,
        UsersManage, RolesManage, SettingsManage,
    };
}
