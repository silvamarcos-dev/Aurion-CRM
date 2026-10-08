using System.Linq.Expressions;
using System.Text.Json;
using Loteadora.Crm.Modules.Audit.Domain;
using Loteadora.Crm.Modules.Crm.Domain;
using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Outbox;
using Loteadora.Crm.Modules.Pipeline.Domain;
using Loteadora.Crm.SharedKernel.Domain;
using Loteadora.Crm.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Loteadora.Crm.Modules.Persistence;

/// <summary>
/// DbContext único do monólito modular. Cada módulo possui seu schema e suas configurações;
/// módulos não consultam tabelas de outros módulos diretamente (verificado por testes de arquitetura).
///
/// Isolamento de tenant (defesa em camadas):
///  1. Global query filter em toda entidade ITenantOwned (falha fechada sem tenant).
///  2. SaveChanges rejeita gravação com OrganizationId diferente do tenant corrente.
///  3. (Planejado) Row Level Security no PostgreSQL.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant, TimeProvider clock)
    : DbContext(options)
{
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

    // Avaliados por consulta (EF parametriza membros do contexto nos query filters).
    private bool HasTenant => tenant.HasTenant;
    private Guid CurrentOrganizationId => tenant.HasTenant ? tenant.OrganizationId : Guid.Empty;

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<SalesPipeline> Pipelines => Set<SalesPipeline>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();

    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadStageTransition> LeadStageTransitions => Set<LeadStageTransition>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(BuildTenantFilter(entityType.ClrType));
                modelBuilder.Entity(entityType.ClrType).HasIndex(nameof(ITenantOwned.OrganizationId));
            }
        }

        // Auditoria também é isolada por tenant na leitura.
        modelBuilder.Entity<AuditEvent>().HasQueryFilter(a => HasTenant && a.OrganizationId == CurrentOrganizationId);
    }

    private LambdaExpression BuildTenantFilter(Type clrType)
    {
        // e => this.HasTenant && e.OrganizationId == this.CurrentOrganizationId
        var param = Expression.Parameter(clrType, "e");
        var ctx = Expression.Constant(this);
        var hasTenant = Expression.Property(ctx, nameof(HasTenant));
        var orgEquals = Expression.Equal(
            Expression.Property(param, nameof(ITenantOwned.OrganizationId)),
            Expression.Property(ctx, nameof(CurrentOrganizationId)));
        return Expression.Lambda(Expression.AndAlso(hasTenant, orgEquals), param);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        ApplyTimestamps(now);
        EnforceTenantOnWrites();
        CollectDomainEventsIntoOutbox(now);

        var pendingAudit = ChangeTracker.Entries<AuditEvent>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (pendingAudit.Count == 0)
            return await base.SaveChangesAsync(cancellationToken);

        var ownsTransaction = Database.CurrentTransaction is null;
        var transaction = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await SealAuditChainAsync(pendingAudit, cancellationToken);
            var result = await base.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    public override int SaveChanges() =>
        throw new NotSupportedException("Use SaveChangesAsync.");

    private void ApplyTimestamps(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<IHasTimestamps>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }

    private void EnforceTenantOnWrites()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (!tenant.HasTenant || entry.Entity.OrganizationId != tenant.OrganizationId)
                throw new InvalidOperationException(
                    $"Gravação cross-tenant bloqueada em {entry.Metadata.ClrType.Name}.");
        }
    }

    private void CollectDomainEventsIntoOutbox(DateTimeOffset now)
    {
        var entities = ChangeTracker.Entries<Entity>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        foreach (var entity in entities)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                var type = domainEvent.GetType();
                OutboxMessages.Add(new OutboxMessage(
                    domainEvent.OrganizationId,
                    type.FullName!,
                    JsonSerializer.Serialize(domainEvent, type, EventJson),
                    now,
                    correlationId: null));
            }
            entity.ClearDomainEvents();
        }
    }

    private async Task SealAuditChainAsync(List<AuditEvent> pending, CancellationToken ct)
    {
        foreach (var group in pending.GroupBy(a => a.OrganizationId))
        {
            // Serializa a cadeia por organização dentro da transação.
            await Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({group.Key.ToString()}, 0))", ct);

            var last = await AuditEvents.IgnoreQueryFilters()
                .Where(a => a.OrganizationId == group.Key)
                .OrderByDescending(a => a.Sequence)
                .Select(a => new { a.Sequence, a.Hash })
                .FirstOrDefaultAsync(ct);

            var sequence = last?.Sequence ?? 0;
            var previousHash = last?.Hash ?? AuditEvent.GenesisHash;

            foreach (var auditEvent in group.OrderBy(a => a.OccurredAt))
            {
                sequence++;
                auditEvent.Seal(sequence, previousHash);
                previousHash = auditEvent.Hash;
            }
        }
    }
}
