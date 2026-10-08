namespace Loteadora.Crm.SharedKernel.Domain;

/// <summary>Base de entidades com identificador UUIDv7 (ordenável por tempo) e eventos de domínio.</summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>Entidade pertencente a uma organização (tenant). Todo acesso é filtrado por OrganizationId no backend.</summary>
public interface ITenantOwned
{
    Guid OrganizationId { get; }
}

public interface IHasTimestamps
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Evento de domínio. É persistido no outbox na mesma transação da mudança de estado.</summary>
public interface IDomainEvent
{
    Guid OrganizationId { get; }
}
