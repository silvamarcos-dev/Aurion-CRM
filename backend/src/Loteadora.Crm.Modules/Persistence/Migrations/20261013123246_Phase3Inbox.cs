using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loteadora.Crm.Modules.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Inbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inbox");

            migrationBuilder.CreateTable(
                name: "channels",
                schema: "inbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    phone_number_e164 = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    external_account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    default_owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    encrypted_access_token = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channels", x => x.id);
                    table.ForeignKey(
                        name: "fk_channels_users_default_owner_user_id",
                        column: x => x.default_owner_user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbound_webhooks",
                schema: "inbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    event_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_webhooks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "inbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_address = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ownership_epoch = table.Column<int>(type: "integer", nullable: false),
                    last_inbound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_outbound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_message_preview = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    last_message_direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    unread_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversations_channels_channel_id",
                        column: x => x.channel_id,
                        principalSchema: "inbox",
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_contacts_contact_id",
                        column: x => x.contact_id,
                        principalSchema: "crm",
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_leads_lead_id",
                        column: x => x.lead_id,
                        principalSchema: "crm",
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "message_templates",
                schema: "inbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    body = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    parameter_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_templates_channels_channel_id",
                        column: x => x.channel_id,
                        principalSchema: "inbox",
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "inbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    author_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_from_lead = table.Column<bool>(type: "boolean", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    template_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    client_message_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "inbox",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channels_default_owner_user_id",
                schema: "inbox",
                table: "channels",
                column: "default_owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_channels_organization_id",
                schema: "inbox",
                table: "channels",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_channels_provider_external_account_id",
                schema: "inbox",
                table: "channels",
                columns: new[] { "provider", "external_account_id" },
                unique: true,
                filter: "external_account_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_channel_id",
                schema: "inbox",
                table: "conversations",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_contact_id",
                schema: "inbox",
                table: "conversations",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_lead_id_channel_id",
                schema: "inbox",
                table: "conversations",
                columns: new[] { "lead_id", "channel_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_organization_id",
                schema: "inbox",
                table: "conversations",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_organization_id_owner_user_id_last_message_at",
                schema: "inbox",
                table: "conversations",
                columns: new[] { "organization_id", "owner_user_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_organization_id_state_last_message_at",
                schema: "inbox",
                table: "conversations",
                columns: new[] { "organization_id", "state", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_owner_user_id",
                schema: "inbox",
                table: "conversations",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_webhooks_available_at",
                schema: "inbox",
                table: "inbound_webhooks",
                column: "available_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_webhooks_provider_event_key",
                schema: "inbox",
                table: "inbound_webhooks",
                columns: new[] { "provider", "event_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_message_templates_channel_id_name_language",
                schema: "inbox",
                table: "message_templates",
                columns: new[] { "channel_id", "name", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_message_templates_organization_id",
                schema: "inbox",
                table: "message_templates",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_client_message_id",
                schema: "inbox",
                table: "messages",
                columns: new[] { "conversation_id", "client_message_id" },
                unique: true,
                filter: "client_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_occurred_at",
                schema: "inbox",
                table: "messages",
                columns: new[] { "conversation_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_organization_id",
                schema: "inbox",
                table: "messages",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_organization_id_external_id",
                schema: "inbox",
                table: "messages",
                columns: new[] { "organization_id", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            // Backfill: capabilities de atendimento nos papéis de sistema existentes (mesmo mapeamento de SystemRoles).
            migrationBuilder.Sql("""
                INSERT INTO iam.role_capabilities (role_id, capability, scope)
                SELECT r.id, c.capability, c.scope
                FROM iam.roles r
                JOIN (VALUES
                    ('Administrador', 'conversations.read', 'Org'), ('Administrador', 'conversations.reply', 'Org'),
                    ('Administrador', 'conversations.assign', 'Org'), ('Administrador', 'channels.manage', 'Org'),
                    ('Administrador', 'channels.simulate', 'Org'),
                    ('Gestor', 'conversations.read', 'Team'), ('Gestor', 'conversations.reply', 'Team'), ('Gestor', 'conversations.assign', 'Team'),
                    ('Corretor', 'conversations.read', 'Own'), ('Corretor', 'conversations.reply', 'Own'),
                    ('Visitante', 'conversations.read', 'Org'),
                    ('Desenvolvedor', 'conversations.read', 'Org')
                ) AS c(role_name, capability, scope) ON c.role_name = r.name
                WHERE r.is_system
                ON CONFLICT (role_id, capability) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM iam.role_capabilities
                WHERE capability IN ('conversations.read','conversations.reply','conversations.assign','channels.manage','channels.simulate');
                """);
            migrationBuilder.DropTable(
                name: "inbound_webhooks",
                schema: "inbox");

            migrationBuilder.DropTable(
                name: "message_templates",
                schema: "inbox");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "inbox");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "inbox");

            migrationBuilder.DropTable(
                name: "channels",
                schema: "inbox");
        }
    }
}
