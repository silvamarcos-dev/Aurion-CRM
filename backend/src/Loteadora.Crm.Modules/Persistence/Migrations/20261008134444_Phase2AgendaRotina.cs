using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loteadora.Crm.Modules.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2AgendaRotina : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.EnsureSchema(
                name: "calendar");

            migrationBuilder.EnsureSchema(
                name: "macros");

            migrationBuilder.EnsureSchema(
                name: "tasks");

            migrationBuilder.CreateTable(
                name: "absences",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absences", x => x.id);
                    table.ForeignKey(
                        name: "fk_absences_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_schedules",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_blocks = table.Column<string>(type: "jsonb", nullable: false),
                    visit_duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    buffer_minutes = table.Column<int>(type: "integer", nullable: false),
                    min_notice_minutes = table.Column<int>(type: "integer", nullable: false),
                    max_visits_per_day = table.Column<int>(type: "integer", nullable: true),
                    works_on_holidays = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_schedules_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointments",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointments_leads_lead_id",
                        column: x => x.lead_id,
                        principalSchema: "crm",
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_users_agent_user_id",
                        column: x => x.agent_user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exceptions",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    end = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exceptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holidays",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    city_ibge_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holidays", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "macros",
                schema: "macros",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shortcut = table.Column<string>(type: "character varying(33)", maxLength: 33, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    development_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_macros", x => x.id);
                    table.ForeignKey(
                        name: "fk_macros_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    city_ibge_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    business_hours = table.Column<string>(type: "jsonb", nullable: false),
                    coverage = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_settings_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "iam",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_tasks_leads_lead_id",
                        column: x => x.lead_id,
                        principalSchema: "crm",
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_users_assignee_user_id",
                        column: x => x.assignee_user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_absences_organization_id",
                schema: "scheduling",
                table: "absences",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_absences_organization_id_user_id_starts_at",
                schema: "scheduling",
                table: "absences",
                columns: new[] { "organization_id", "user_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_absences_user_id",
                schema: "scheduling",
                table: "absences",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_schedules_organization_id",
                schema: "scheduling",
                table: "agent_schedules",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_schedules_organization_id_user_id",
                schema: "scheduling",
                table: "agent_schedules",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_schedules_user_id",
                schema: "scheduling",
                table: "agent_schedules",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_agent_user_id",
                schema: "scheduling",
                table: "appointments",
                column: "agent_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_lead_id",
                schema: "scheduling",
                table: "appointments",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_organization_id",
                schema: "scheduling",
                table: "appointments",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_organization_id_agent_user_id_starts_at",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "organization_id", "agent_user_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_organization_id_lead_id",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "organization_id", "lead_id" });

            migrationBuilder.CreateIndex(
                name: "ix_exceptions_organization_id",
                schema: "calendar",
                table: "exceptions",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_exceptions_organization_id_date",
                schema: "calendar",
                table: "exceptions",
                columns: new[] { "organization_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holidays_organization_id",
                schema: "calendar",
                table: "holidays",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_holidays_organization_id_date_scope_state_code_city_ibge_co",
                schema: "calendar",
                table: "holidays",
                columns: new[] { "organization_id", "date", "scope", "state_code", "city_ibge_code" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_macros_organization_id",
                schema: "macros",
                table: "macros",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_macros_organization_id_owner_user_id_shortcut",
                schema: "macros",
                table: "macros",
                columns: new[] { "organization_id", "owner_user_id", "shortcut" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_macros_owner_user_id",
                schema: "macros",
                table: "macros",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_settings_organization_id",
                schema: "calendar",
                table: "settings",
                column: "organization_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_assignee_user_id",
                schema: "tasks",
                table: "tasks",
                column: "assignee_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_lead_id",
                schema: "tasks",
                table: "tasks",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_organization_id",
                schema: "tasks",
                table: "tasks",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_organization_id_assignee_user_id_status_due_at",
                schema: "tasks",
                table: "tasks",
                columns: new[] { "organization_id", "assignee_user_id", "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_organization_id_lead_id_status",
                schema: "tasks",
                table: "tasks",
                columns: new[] { "organization_id", "lead_id", "status" });

            // Anti double-booking: dois compromissos AGENDADOS do mesmo corretor nunca se sobrepõem,
            // mesmo com bug na aplicação ou requisições simultâneas. btree_gist é extensão "trusted" (dono do banco pode criar).
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;

                ALTER TABLE scheduling.appointments
                    ADD CONSTRAINT ex_appointments_agent_no_overlap
                    EXCLUDE USING gist (
                        organization_id WITH =,
                        agent_user_id WITH =,
                        tstzrange(starts_at, ends_at, '[)') WITH &&
                    ) WHERE (status = 'Scheduled');

                ALTER TABLE scheduling.appointments
                    ADD CONSTRAINT ck_appointments_range CHECK (ends_at > starts_at);
                ALTER TABLE scheduling.absences
                    ADD CONSTRAINT ck_absences_range CHECK (ends_at > starts_at);
                """);

            // Backfill para organizações existentes: novas capabilities nos papéis de sistema
            // (mesmo mapeamento de SystemRoles) e calendário padrão.
            migrationBuilder.Sql("""
                INSERT INTO iam.role_capabilities (role_id, capability, scope)
                SELECT r.id, c.capability, c.scope
                FROM iam.roles r
                JOIN (VALUES
                    ('Administrador', 'calendar.read', 'Org'), ('Administrador', 'calendar.manage', 'Org'),
                    ('Administrador', 'tasks.read', 'Org'), ('Administrador', 'tasks.manage', 'Org'),
                    ('Administrador', 'macros.use', 'Org'), ('Administrador', 'macros.manage', 'Org'),
                    ('Gestor', 'calendar.read', 'Team'), ('Gestor', 'calendar.manage', 'Team'),
                    ('Gestor', 'tasks.read', 'Team'), ('Gestor', 'tasks.manage', 'Team'),
                    ('Gestor', 'macros.use', 'Org'), ('Gestor', 'macros.manage', 'Org'),
                    ('Corretor', 'calendar.read', 'Own'), ('Corretor', 'calendar.manage', 'Own'),
                    ('Corretor', 'tasks.read', 'Own'), ('Corretor', 'tasks.manage', 'Own'),
                    ('Corretor', 'macros.use', 'Org'), ('Corretor', 'macros.manage', 'Own')
                ) AS c(role_name, capability, scope) ON c.role_name = r.name
                WHERE r.is_system
                ON CONFLICT (role_id, capability) DO NOTHING;

                INSERT INTO calendar.settings (id, organization_id, time_zone, business_hours, coverage, created_at, updated_at)
                SELECT gen_random_uuid(), o.id, o.time_zone,
                       '[{"isoWeekday":1,"start":"08:00:00","end":"18:00:00"},{"isoWeekday":2,"start":"08:00:00","end":"18:00:00"},{"isoWeekday":3,"start":"08:00:00","end":"18:00:00"},{"isoWeekday":4,"start":"08:00:00","end":"18:00:00"},{"isoWeekday":5,"start":"08:00:00","end":"18:00:00"}]'::jsonb,
                       '{"offHours":true,"saturdays":true,"sundays":true,"holidays":true}'::jsonb,
                       now(), now()
                FROM iam.organizations o
                WHERE NOT EXISTS (SELECT 1 FROM calendar.settings s WHERE s.organization_id = o.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM iam.role_capabilities
                WHERE capability IN ('calendar.read','calendar.manage','tasks.read','tasks.manage','macros.use','macros.manage');
                """);

            migrationBuilder.DropTable(
                name: "absences",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "agent_schedules",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "appointments",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "exceptions",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "holidays",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "macros",
                schema: "macros");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "tasks",
                schema: "tasks");
        }
    }
}
