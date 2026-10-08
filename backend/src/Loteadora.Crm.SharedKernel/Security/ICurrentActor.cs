namespace Loteadora.Crm.SharedKernel.Security;

public enum ActorKind
{
    User = 1,
    Copilot = 2,
    System = 3,
}

/// <summary>
/// Quem está executando a ação, sempre dentro de UMA organização.
/// OrganizationId vem do token/sessão validados no backend — nunca de parâmetros enviados pelo cliente.
/// </summary>
public interface ICurrentActor
{
    bool IsAuthenticated { get; }
    ActorKind Kind { get; }
    Guid UserId { get; }
    Guid OrganizationId { get; }
    Guid? TeamId { get; }

    /// <summary>Retorna o escopo concedido para a capability, ou null se não concedida.</summary>
    CapabilityScope? ScopeOf(string capability);
}

public static class CurrentActorExtensions
{
    public static bool Has(this ICurrentActor actor, string capability) => actor.ScopeOf(capability) is not null;

    /// <summary>Exige a capability e retorna o escopo concedido.</summary>
    public static CapabilityScope Require(this ICurrentActor actor, string capability) =>
        actor.ScopeOf(capability) ?? throw new Domain.ForbiddenException(capability);
}
