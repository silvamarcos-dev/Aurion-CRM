namespace Loteadora.Crm.SharedKernel.Tenancy;

/// <summary>
/// Organização corrente para filtros de persistência. Quando não há tenant (ex.: login, jobs globais)
/// HasTenant = false e as consultas a dados de tenant não retornam nada (falha fechada).
/// </summary>
public interface ITenantContext
{
    bool HasTenant { get; }
    Guid OrganizationId { get; }
}
