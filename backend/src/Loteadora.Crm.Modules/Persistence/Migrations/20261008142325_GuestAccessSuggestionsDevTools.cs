using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loteadora.Crm.Modules.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GuestAccessSuggestionsDevTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "feedback");

            migrationBuilder.AddColumn<bool>(
                name: "is_guest",
                schema: "iam",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "iam",
                table: "memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "access_links",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guest_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    use_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_links_users_created_by_id",
                        column: x => x.created_by_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_links_users_guest_user_id",
                        column: x => x.guest_user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "suggestions",
                schema: "feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    page_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    page_title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    element_descriptor = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    viewport = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_suggestions_users_author_user_id",
                        column: x => x.author_user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "suggestion_comments",
                schema: "feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    suggestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggestion_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_suggestion_comments_suggestions_suggestion_id",
                        column: x => x.suggestion_id,
                        principalSchema: "feedback",
                        principalTable: "suggestions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_suggestion_comments_users_author_user_id",
                        column: x => x.author_user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_links_created_by_id",
                schema: "iam",
                table: "access_links",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_links_guest_user_id",
                schema: "iam",
                table: "access_links",
                column: "guest_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_links_organization_id",
                schema: "iam",
                table: "access_links",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_links_token_hash",
                schema: "iam",
                table: "access_links",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suggestion_comments_author_user_id",
                schema: "feedback",
                table: "suggestion_comments",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_suggestion_comments_organization_id",
                schema: "feedback",
                table: "suggestion_comments",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_suggestion_comments_suggestion_id_created_at",
                schema: "feedback",
                table: "suggestion_comments",
                columns: new[] { "suggestion_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_suggestions_author_user_id",
                schema: "feedback",
                table: "suggestions",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_suggestions_organization_id",
                schema: "feedback",
                table: "suggestions",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_suggestions_organization_id_author_user_id",
                schema: "feedback",
                table: "suggestions",
                columns: new[] { "organization_id", "author_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_suggestions_organization_id_status_created_at",
                schema: "feedback",
                table: "suggestions",
                columns: new[] { "organization_id", "status", "created_at" });

            // Backfill para organizações existentes (mesmo mapeamento de SystemRoles.GrantsFor):
            // 1) papéis de sistema Visitante e Desenvolvedor;
            // 2) novas capabilities nos papéis existentes.
            migrationBuilder.Sql("""
                INSERT INTO iam.roles (id, organization_id, name, is_system, created_at, updated_at)
                SELECT gen_random_uuid(), o.id, r.name, true, now(), now()
                FROM iam.organizations o
                CROSS JOIN (VALUES ('Visitante'), ('Desenvolvedor')) AS r(name)
                WHERE NOT EXISTS (SELECT 1 FROM iam.roles x WHERE x.organization_id = o.id AND x.name = r.name);

                INSERT INTO iam.role_capabilities (role_id, capability, scope)
                SELECT r.id, c.capability, c.scope
                FROM iam.roles r
                JOIN (VALUES
                    ('Administrador', 'pii.view', 'Org'), ('Administrador', 'dev.tools', 'Org'),
                    ('Administrador', 'suggestions.create', 'Org'), ('Administrador', 'suggestions.read', 'Org'), ('Administrador', 'suggestions.manage', 'Org'),
                    ('Gestor', 'pii.view', 'Org'), ('Gestor', 'suggestions.create', 'Own'), ('Gestor', 'suggestions.read', 'Own'),
                    ('Corretor', 'pii.view', 'Org'), ('Corretor', 'suggestions.create', 'Own'), ('Corretor', 'suggestions.read', 'Own'),
                    ('Visitante', 'leads.read', 'Org'), ('Visitante', 'calendar.read', 'Org'), ('Visitante', 'tasks.read', 'Org'),
                    ('Visitante', 'macros.use', 'Org'), ('Visitante', 'suggestions.create', 'Own'), ('Visitante', 'suggestions.read', 'Own'),
                    ('Desenvolvedor', 'leads.read', 'Org'), ('Desenvolvedor', 'calendar.read', 'Org'), ('Desenvolvedor', 'tasks.read', 'Org'),
                    ('Desenvolvedor', 'macros.use', 'Org'), ('Desenvolvedor', 'audit.read', 'Org'), ('Desenvolvedor', 'dev.tools', 'Org'),
                    ('Desenvolvedor', 'suggestions.create', 'Own'), ('Desenvolvedor', 'suggestions.read', 'Org'), ('Desenvolvedor', 'suggestions.manage', 'Org')
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
                WHERE capability IN ('pii.view','dev.tools','suggestions.create','suggestions.read','suggestions.manage');
                DELETE FROM iam.role_capabilities rc USING iam.roles r
                WHERE rc.role_id = r.id AND r.is_system AND r.name IN ('Visitante','Desenvolvedor');
                DELETE FROM iam.roles r
                WHERE r.is_system AND r.name IN ('Visitante','Desenvolvedor')
                  AND NOT EXISTS (SELECT 1 FROM iam.memberships m WHERE m.role_id = r.id);
                """);

            migrationBuilder.DropTable(
                name: "access_links",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "suggestion_comments",
                schema: "feedback");

            migrationBuilder.DropTable(
                name: "suggestions",
                schema: "feedback");

            migrationBuilder.DropColumn(
                name: "is_guest",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "iam",
                table: "memberships");
        }
    }
}
