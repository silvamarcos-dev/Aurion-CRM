using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Suggestions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Loteadora.Crm.Modules.Persistence;

internal sealed class AccessLinkConfiguration : IEntityTypeConfiguration<AccessLink>
{
    public void Configure(EntityTypeBuilder<AccessLink> b)
    {
        b.ToTable("access_links", "iam");
        b.Property(x => x.RoleName).HasMaxLength(80);
        b.Property(x => x.Label).HasMaxLength(120);
        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.GuestUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ChangeSuggestionConfiguration : IEntityTypeConfiguration<ChangeSuggestion>
{
    public void Configure(EntityTypeBuilder<ChangeSuggestion> b)
    {
        b.ToTable("suggestions", "feedback");
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Title).HasMaxLength(140);
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.PagePath).HasMaxLength(300);
        b.Property(x => x.PageTitle).HasMaxLength(140);
        b.Property(x => x.ElementDescriptor).HasMaxLength(600);
        b.Property(x => x.Viewport).HasMaxLength(20);
        b.HasIndex(x => new { x.OrganizationId, x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.OrganizationId, x.AuthorUserId });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SuggestionCommentConfiguration : IEntityTypeConfiguration<SuggestionComment>
{
    public void Configure(EntityTypeBuilder<SuggestionComment> b)
    {
        b.ToTable("suggestion_comments", "feedback");
        b.Property(x => x.Body).HasMaxLength(2000);
        b.HasIndex(x => new { x.SuggestionId, x.CreatedAt });
        b.HasOne<ChangeSuggestion>().WithMany().HasForeignKey(x => x.SuggestionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
