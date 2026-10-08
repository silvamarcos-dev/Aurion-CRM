using Loteadora.Crm.Modules.Audit.Domain;
using Loteadora.Crm.Modules.Crm.Domain;
using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Outbox;
using Loteadora.Crm.Modules.Pipeline.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Loteadora.Crm.Modules.Persistence;

internal static class Schemas
{
    public const string Iam = "iam";
    public const string Crm = "crm";
    public const string Pipeline = "pipeline";
    public const string Audit = "audit";
    public const string Messaging = "messaging";
}

// ───────────────────────────── IAM ─────────────────────────────

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.ToTable("organizations", Schemas.Iam);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Slug).HasMaxLength(80);
        b.Property(x => x.TimeZone).HasMaxLength(64);
        b.HasIndex(x => x.Slug).IsUnique();
    }
}

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> b)
    {
        b.ToTable("teams", Schemas.Iam);
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", Schemas.Iam);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.DisplayName).HasMaxLength(160);
        b.Property(x => x.PasswordHash).HasMaxLength(512);
        b.HasIndex(x => x.Email).IsUnique();
        b.Ignore(x => x.FirstName);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles", Schemas.Iam);
        b.Property(x => x.Name).HasMaxLength(80);
        b.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.OwnsMany(x => x.Capabilities, c =>
        {
            c.ToTable("role_capabilities", Schemas.Iam);
            c.WithOwner().HasForeignKey(x => x.RoleId);
            c.HasKey(x => new { x.RoleId, x.Capability });
            c.Property(x => x.Capability).HasMaxLength(80);
            c.Property(x => x.Scope).HasConversion<string>().HasMaxLength(16);
        });
        b.Navigation(x => x.Capabilities).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> b)
    {
        b.ToTable("memberships", Schemas.Iam);
        b.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        b.HasIndex(x => x.UserId);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens", Schemas.Iam);
        b.Property(x => x.TokenHash).HasMaxLength(128);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.FamilyId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.DomainEvents);
    }
}

// ───────────────────────────── Pipeline ─────────────────────────────

internal sealed class PipelineConfiguration : IEntityTypeConfiguration<SalesPipeline>
{
    public void Configure(EntityTypeBuilder<SalesPipeline> b)
    {
        b.ToTable("pipelines", Schemas.Pipeline);
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasMany(x => x.Stages).WithOne().HasForeignKey(x => x.PipelineId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Stages).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class PipelineStageConfiguration : IEntityTypeConfiguration<PipelineStage>
{
    public void Configure(EntityTypeBuilder<PipelineStage> b)
    {
        b.ToTable("stages", Schemas.Pipeline);
        b.Property(x => x.Name).HasMaxLength(80);
        b.Property(x => x.Color).HasMaxLength(16);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(x => new { x.PipelineId, x.Position });
        b.Ignore(x => x.DomainEvents);
    }
}

// ───────────────────────────── CRM ─────────────────────────────

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> b)
    {
        b.ToTable("contacts", Schemas.Crm);
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.PhoneE164).HasMaxLength(16);
        b.Property(x => x.Email).HasMaxLength(254);
        b.HasIndex(x => new { x.OrganizationId, x.PhoneE164 });
        b.HasIndex(x => new { x.OrganizationId, x.Email });
        b.Ignore(x => x.FirstName);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> b)
    {
        b.ToTable("leads", Schemas.Crm);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Timeframe).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Source).HasMaxLength(80);
        b.Property(x => x.Campaign).HasMaxLength(120);
        b.Property(x => x.Interest).HasMaxLength(500);
        b.Property(x => x.Region).HasMaxLength(120);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.LostReason).HasMaxLength(500);
        b.Property(x => x.Version).IsRowVersion();

        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesPipeline>().WithMany().HasForeignKey(x => x.PipelineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PipelineStage>().WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.OrganizationId, x.PipelineId, x.StageId });
        b.HasIndex(x => new { x.OrganizationId, x.OwnerUserId, x.Status });
        b.HasIndex(x => new { x.OrganizationId, x.NextFollowUpAt });
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class LeadStageTransitionConfiguration : IEntityTypeConfiguration<LeadStageTransition>
{
    public void Configure(EntityTypeBuilder<LeadStageTransition> b)
    {
        b.ToTable("lead_stage_transitions", Schemas.Crm);
        b.Property(x => x.ActorKind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.LeadId, x.OccurredAt });
        b.Ignore(x => x.DomainEvents);
    }
}

// ───────────────────────────── Auditoria / Outbox ─────────────────────────────

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("audit_events", Schemas.Audit);
        b.HasKey(x => x.Id);
        b.Property(x => x.ActorKind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Action).HasMaxLength(80);
        b.Property(x => x.EntityType).HasMaxLength(80);
        // "json" (e não "jsonb") preserva o texto exato — necessário para a verificação do hash encadeado.
        b.Property(x => x.Data).HasColumnType("json");
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.Property(x => x.PreviousHash).HasMaxLength(64).IsFixedLength();
        b.Property(x => x.Hash).HasMaxLength(64).IsFixedLength();
        b.HasIndex(x => new { x.OrganizationId, x.Sequence }).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.EntityType, x.EntityId, x.OccurredAt });
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages", Schemas.Messaging);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasMaxLength(300);
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.LastError).HasMaxLength(2000);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.HasIndex(x => x.AvailableAt).HasFilter("processed_at IS NULL");
        b.Ignore(x => x.IsDeadLettered);
    }
}
