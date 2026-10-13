using Loteadora.Crm.Modules.Crm.Domain;
using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Inbox.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Loteadora.Crm.Modules.Persistence;

internal sealed class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> b)
    {
        b.ToTable("channels", "inbox");
        b.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Name).HasMaxLength(80);
        b.Property(x => x.PhoneNumberE164).HasMaxLength(16);
        b.Property(x => x.ExternalAccountId).HasMaxLength(64);
        b.Property(x => x.EncryptedAccessToken).HasMaxLength(4000);
        b.Ignore(x => x.HasCredentials);
        // Phone Number ID identifica o tenant no webhook: único globalmente.
        b.HasIndex(x => new { x.Provider, x.ExternalAccountId }).IsUnique().HasFilter("external_account_id IS NOT NULL");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.DefaultOwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> b)
    {
        b.ToTable("message_templates", "inbox");
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Language).HasMaxLength(10);
        b.Property(x => x.Body).HasMaxLength(1024);
        b.HasIndex(x => new { x.ChannelId, x.Name, x.Language }).IsUnique();
        b.HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> b)
    {
        b.ToTable("conversations", "inbox");
        b.Property(x => x.ContactAddress).HasMaxLength(16);
        b.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.OwnerKind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.LastMessageDirection).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.LastMessagePreview).HasMaxLength(160);
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.CustomerServiceWindowEndsAt);
        b.HasIndex(x => new { x.LeadId, x.ChannelId }).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.OwnerUserId, x.LastMessageAt });
        b.HasIndex(x => new { x.OrganizationId, x.State, x.LastMessageAt });
        b.HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.ToTable("messages", "inbox");
        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.AuthorKind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Body).HasMaxLength(4096);
        b.Property(x => x.TemplateName).HasMaxLength(120);
        b.Property(x => x.ExternalId).HasMaxLength(128);
        b.Property(x => x.ClientMessageId).HasMaxLength(64);
        b.Property(x => x.FailureReason).HasMaxLength(300);
        b.HasIndex(x => new { x.ConversationId, x.OccurredAt });
        // Idempotência: mesmo wamid nunca entra duas vezes; mesmo envio do cliente nunca duplica.
        b.HasIndex(x => new { x.OrganizationId, x.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL");
        b.HasIndex(x => new { x.ConversationId, x.ClientMessageId }).IsUnique().HasFilter("client_message_id IS NOT NULL");
        b.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InboundWebhookConfiguration : IEntityTypeConfiguration<InboundWebhook>
{
    public void Configure(EntityTypeBuilder<InboundWebhook> b)
    {
        b.ToTable("inbound_webhooks", "inbox");
        b.HasKey(x => x.Id);
        b.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.EventKey).HasMaxLength(64);
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.HasIndex(x => new { x.Provider, x.EventKey }).IsUnique();
        b.HasIndex(x => x.AvailableAt).HasFilter("processed_at IS NULL");
    }
}
