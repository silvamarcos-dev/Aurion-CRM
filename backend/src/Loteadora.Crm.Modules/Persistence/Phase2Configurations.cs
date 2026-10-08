using System.Text.Json;
using Loteadora.Crm.Modules.BusinessCalendar.Domain;
using Loteadora.Crm.Modules.Crm.Domain;
using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Macros.Domain;
using Loteadora.Crm.Modules.Scheduling.Domain;
using Loteadora.Crm.Modules.Tasks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Loteadora.Crm.Modules.Persistence;

/// <summary>Conversão JSON (jsonb) para valores pequenos e imutáveis (intervalos semanais, regras de cobertura).</summary>
internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<List<WeeklyInterval>> AsJsonIntervals(this PropertyBuilder<List<WeeklyInterval>> property) =>
        property.HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<List<WeeklyInterval>>(v, Options) ?? new List<WeeklyInterval>(),
            new ValueComparer<List<WeeklyInterval>>(
                (a, b) => (a ?? new List<WeeklyInterval>()).SequenceEqual(b ?? new List<WeeklyInterval>()),
                v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode())),
                v => v.ToList()));

    public static PropertyBuilder<CopilotCoverageRules> AsJsonCoverage(this PropertyBuilder<CopilotCoverageRules> property) =>
        property.HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<CopilotCoverageRules>(v, Options) ?? CopilotCoverageRules.Default);
}

internal static class Phase2Schemas
{
    public const string Calendar = "calendar";
    public const string Scheduling = "scheduling";
    public const string Tasks = "tasks";
    public const string Macros = "macros";
}

internal sealed class CalendarSettingsConfiguration : IEntityTypeConfiguration<CalendarSettings>
{
    public void Configure(EntityTypeBuilder<CalendarSettings> b)
    {
        b.ToTable("settings", Phase2Schemas.Calendar);
        b.HasIndex(x => x.OrganizationId).IsUnique();
        b.Property(x => x.TimeZone).HasMaxLength(64);
        b.Property(x => x.StateCode).HasMaxLength(2);
        b.Property(x => x.CityIbgeCode).HasMaxLength(7);
        b.Property(x => x.BusinessHours).AsJsonIntervals();
        b.Property(x => x.Coverage).AsJsonCoverage();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> b)
    {
        b.ToTable("holidays", Phase2Schemas.Calendar);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Scope).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.StateCode).HasMaxLength(2);
        b.Property(x => x.CityIbgeCode).HasMaxLength(7);
        b.HasIndex(x => new { x.OrganizationId, x.Date, x.Scope, x.StateCode, x.CityIbgeCode }).IsUnique().AreNullsDistinct(false);
    }
}

internal sealed class CalendarExceptionConfiguration : IEntityTypeConfiguration<CalendarException>
{
    public void Configure(EntityTypeBuilder<CalendarException> b)
    {
        b.ToTable("exceptions", Phase2Schemas.Calendar);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasIndex(x => new { x.OrganizationId, x.Date }).IsUnique();
    }
}

internal sealed class AgentScheduleConfiguration : IEntityTypeConfiguration<AgentSchedule>
{
    public void Configure(EntityTypeBuilder<AgentSchedule> b)
    {
        b.ToTable("agent_schedules", Phase2Schemas.Scheduling);
        b.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        b.Property(x => x.WorkBlocks).AsJsonIntervals();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AbsenceConfiguration : IEntityTypeConfiguration<Absence>
{
    public void Configure(EntityTypeBuilder<Absence> b)
    {
        b.ToTable("absences", Phase2Schemas.Scheduling);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasIndex(x => new { x.OrganizationId, x.UserId, x.StartsAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.ToTable("appointments", Phase2Schemas.Scheduling);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.CreatedByKind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Title).HasMaxLength(160);
        b.Property(x => x.Location).HasMaxLength(300);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.CancelReason).HasMaxLength(300);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => new { x.OrganizationId, x.AgentUserId, x.StartsAt });
        b.HasIndex(x => new { x.OrganizationId, x.LeadId });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AgentUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        // EXCLUDE constraint (anti double-booking) criada via SQL na migration.
    }
}

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> b)
    {
        b.ToTable("tasks", Phase2Schemas.Tasks);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.CreatedByKind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Title).HasMaxLength(160);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.OrganizationId, x.AssigneeUserId, x.Status, x.DueAt });
        b.HasIndex(x => new { x.OrganizationId, x.LeadId, x.Status });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AssigneeUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MacroConfiguration : IEntityTypeConfiguration<Macro>
{
    public void Configure(EntityTypeBuilder<Macro> b)
    {
        b.ToTable("macros", Phase2Schemas.Macros);
        b.Property(x => x.Shortcut).HasMaxLength(33);
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.Body).HasMaxLength(4000);
        b.Ignore(x => x.IsShared);
        b.HasIndex(x => new { x.OrganizationId, x.OwnerUserId, x.Shortcut }).IsUnique().AreNullsDistinct(false);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
