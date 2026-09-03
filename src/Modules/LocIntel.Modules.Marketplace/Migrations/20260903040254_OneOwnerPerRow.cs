using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Marketplace.Migrations
{
    /// <summary>
    /// ADR 48: every row gets exactly one owner. The requester keeps the
    /// request, its recipient list, its timeline copies and a projection of
    /// each quote; the vendor gets its own assignment row, its quote, its
    /// timeline copies; published profiles project into a platform-global
    /// directory. Existing multi-owner rows are split into each side's own
    /// rows before the shared columns and cross-tenant policies go.
    /// </summary>
    public partial class OneOwnerPerRow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_vendor_profiles_published",
                schema: "marketplace",
                table: "vendor_profiles"
            );

            migrationBuilder.DropIndex(
                name: "IX_requests_vendor_org_id_status_updated_at",
                schema: "marketplace",
                table: "requests"
            );

            migrationBuilder.DropIndex(
                name: "IX_quotes_request_id_vendor_org_id",
                schema: "marketplace",
                table: "quotes"
            );

            migrationBuilder.RenameColumn(
                name: "vendor_org_id",
                schema: "marketplace",
                table: "quotes",
                newName: "requester_org_id"
            );
            migrationBuilder.Sql(
                """
                -- after the rename requester_org_id holds the VENDOR and org_id the requester: swap
                UPDATE "marketplace"."quotes" SET org_id = requester_org_id, requester_org_id = org_id;
                """
            );

            migrationBuilder.AddColumn<Guid>(
                name: "source_id",
                schema: "marketplace",
                table: "request_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );
            migrationBuilder.Sql(
                """
                UPDATE "marketplace"."request_events" SET source_id = id;
                INSERT INTO "marketplace"."request_events"
                    (id, org_id, source_id, request_id, actor_org_id, actor_id, kind, body, latitude, longitude, distance_from_site_m, at)
                SELECT gen_random_uuid(), e.vendor_org_id, e.id, e.request_id, e.actor_org_id, e.actor_id, e.kind, e.body,
                       e.latitude, e.longitude, e.distance_from_site_m, e.at
                FROM "marketplace"."request_events" e
                WHERE e.vendor_org_id IS NOT NULL AND e.vendor_org_id <> e.org_id;
                """
            );

            migrationBuilder.CreateTable(
                name: "received_quotes",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    amount = table.Column<decimal>(
                        type: "numeric(14,2)",
                        precision: 14,
                        scale: 2,
                        nullable: false
                    ),
                    currency = table.Column<string>(
                        type: "character varying(3)",
                        maxLength: 3,
                        nullable: false
                    ),
                    notes = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                    valid_until = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    submitted_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_received_quotes", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "vendor_assignments",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    mode = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    category = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false
                    ),
                    urgency = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    participation = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    site_time_zone = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    site_latitude = table.Column<double>(type: "double precision", nullable: true),
                    site_longitude = table.Column<double>(type: "double precision", nullable: true),
                    site_country_code = table.Column<string>(
                        type: "character varying(2)",
                        maxLength: 2,
                        nullable: true
                    ),
                    title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    details = table.Column<string>(type: "text", nullable: false),
                    spec = table.Column<string>(type: "jsonb", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ends_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    rrule = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    budget_amount = table.Column<decimal>(
                        type: "numeric(14,2)",
                        precision: 14,
                        scale: 2,
                        nullable: true
                    ),
                    currency = table.Column<string>(
                        type: "character varying(3)",
                        maxLength: 3,
                        nullable: false
                    ),
                    submitted_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    response_due_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    escalated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    escalation_count = table.Column<int>(type: "integer", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    decline_reason = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: true
                    ),
                    started_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    completed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    completion_summary = table.Column<string>(
                        type: "character varying(4000)",
                        maxLength: 4000,
                        nullable: true
                    ),
                    verified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    dispute_reason = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                    cancelled_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    cancel_reason = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: true
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_assignments", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "vendor_directory",
                schema: "marketplace",
                columns: table => new
                {
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    description = table.Column<string>(type: "text", nullable: false),
                    categories = table.Column<string[]>(type: "text[]", nullable: false),
                    service_areas = table.Column<string[]>(type: "text[]", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    service_radius_km = table.Column<double>(
                        type: "double precision",
                        nullable: true
                    ),
                    contact_email = table.Column<string>(
                        type: "character varying(320)",
                        maxLength: 320,
                        nullable: true
                    ),
                    contact_phone = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: true
                    ),
                    credentials = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_directory", x => x.org_id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_events_org_id_source_id",
                schema: "marketplace",
                table: "request_events",
                columns: new[] { "org_id", "source_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_quotes_org_id_request_id",
                schema: "marketplace",
                table: "quotes",
                columns: new[] { "org_id", "request_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_received_quotes_org_id_quote_id",
                schema: "marketplace",
                table: "received_quotes",
                columns: new[] { "org_id", "quote_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_received_quotes_request_id_vendor_org_id",
                schema: "marketplace",
                table: "received_quotes",
                columns: new[] { "request_id", "vendor_org_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_vendor_assignments_org_id_request_id",
                schema: "marketplace",
                table: "vendor_assignments",
                columns: new[] { "org_id", "request_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_vendor_assignments_org_id_status_updated_at",
                schema: "marketplace",
                table: "vendor_assignments",
                columns: new[] { "org_id", "status", "updated_at" }
            );
            migrationBuilder.Sql(
                """
                -- the requester's projection of every quote it had received
                INSERT INTO "marketplace"."received_quotes"
                    (id, org_id, request_id, quote_id, vendor_org_id, vendor_name, amount, currency, notes, valid_until, status, submitted_at, updated_at)
                SELECT gen_random_uuid(), q.requester_org_id, q.request_id, q.id, q.org_id, COALESCE(p.name, 'Vendor'),
                       q.amount, q.currency, q.notes, q.valid_until, q.status, q.created_at, q.updated_at
                FROM "marketplace"."quotes" q
                LEFT JOIN "marketplace"."vendor_profiles" p ON p.org_id = q.org_id;
                -- each vendor's own row for every request it was chosen for or invited to
                INSERT INTO "marketplace"."vendor_assignments"
                    (id, org_id, request_id, requester_org_id, requester_name, mode, category, urgency, status, participation,
                     site_id, site_name, site_time_zone, site_latitude, site_longitude, site_country_code, title, details, spec,
                     starts_at, ends_at, rrule, budget_amount, currency, submitted_at, response_due_at, escalated_at, escalation_count,
                     accepted_at, decline_reason, started_at, completed_at, completion_summary, verified_at, dispute_reason,
                     cancelled_at, cancel_reason, created_at, updated_at)
                SELECT gen_random_uuid(), v.vendor_org_id, r.id, r.org_id, r.requester_name, r.mode, r.category, r.urgency, r.status,
                       CASE WHEN r.vendor_org_id = v.vendor_org_id THEN 'Assigned'
                            WHEN r.vendor_org_id IS NOT NULL THEN 'NotSelected'
                            ELSE COALESCE(v.recipient_status, 'Invited') END,
                       r.site_id, r.site_name, r.site_time_zone, r.site_latitude, r.site_longitude, r.site_country_code, r.title,
                       r.details, r.spec, r.starts_at, r.ends_at, r.rrule, r.budget_amount, r.currency, r.submitted_at,
                       r.response_due_at, r.escalated_at, r.escalation_count, r.accepted_at, r.decline_reason, r.started_at,
                       r.completed_at, r.completion_summary, r.verified_at, r.dispute_reason, r.cancelled_at, r.cancel_reason,
                       r.created_at, r.updated_at
                FROM "marketplace"."requests" r
                JOIN (
                    SELECT request_id, vendor_org_id, status AS recipient_status FROM "marketplace"."request_recipients"
                    UNION
                    SELECT id, vendor_org_id, NULL FROM "marketplace"."requests" WHERE vendor_org_id IS NOT NULL
                ) v ON v.request_id = r.id
                WHERE r.status <> 'Draft';
                -- the platform-global directory: every published profile, with its public credential summary
                INSERT INTO "marketplace"."vendor_directory"
                    (org_id, name, description, categories, service_areas, latitude, longitude, service_radius_km,
                     contact_email, contact_phone, credentials, updated_at)
                SELECT p.org_id, p.name, p.description, p.categories, p.service_areas, p.latitude, p.longitude, p.service_radius_km,
                       p.contact_email, p.contact_phone,
                       COALESCE((SELECT json_agg(json_build_object('Id', c.id, 'Kind', c.kind, 'Label', c.label,
                                                                   'Jurisdiction', c.jurisdiction, 'ExpiresAt', c.expires_at)
                                                 ORDER BY c.expires_at)
                                 FROM "marketplace"."vendor_credentials" c WHERE c.org_id = p.org_id), '[]'::json)::jsonb,
                       p.updated_at
                FROM "marketplace"."vendor_profiles" p
                WHERE p.published;
                -- one owner per row: the cross-tenant policies go
                DROP POLICY tenant_isolation ON "marketplace"."requests";
                DROP POLICY tenant_isolation ON "marketplace"."request_events";
                DROP POLICY tenant_isolation ON "marketplace"."request_recipients";
                DROP POLICY tenant_isolation ON "marketplace"."quotes";
                DROP POLICY catalog_read ON "marketplace"."vendor_profiles";
                DROP POLICY owner_write ON "marketplace"."vendor_profiles";
                DROP POLICY catalog_read ON "marketplace"."vendor_credentials";
                DROP POLICY owner_write ON "marketplace"."vendor_credentials";
                """
            );
            migrationBuilder.EnableTenantRls("marketplace", "requests");
            migrationBuilder.EnableTenantRls("marketplace", "request_events");
            migrationBuilder.EnableTenantRls("marketplace", "request_recipients");
            migrationBuilder.EnableTenantRls("marketplace", "quotes");
            migrationBuilder.EnableTenantRls("marketplace", "vendor_profiles");
            migrationBuilder.EnableTenantRls("marketplace", "vendor_credentials");
            migrationBuilder.EnableTenantRls("marketplace", "received_quotes");
            migrationBuilder.EnableTenantRls("marketplace", "vendor_assignments");
            migrationBuilder.DropColumn(
                name: "vendor_org_id",
                schema: "marketplace",
                table: "request_events"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "marketplace"."request_events" WHERE id <> source_id;
                """
            );
            migrationBuilder.DropTable(name: "received_quotes", schema: "marketplace");

            migrationBuilder.DropTable(name: "vendor_assignments", schema: "marketplace");

            migrationBuilder.DropTable(name: "vendor_directory", schema: "marketplace");

            migrationBuilder.DropIndex(
                name: "IX_request_events_org_id_source_id",
                schema: "marketplace",
                table: "request_events"
            );

            migrationBuilder.DropIndex(
                name: "IX_quotes_org_id_request_id",
                schema: "marketplace",
                table: "quotes"
            );

            migrationBuilder.DropColumn(
                name: "source_id",
                schema: "marketplace",
                table: "request_events"
            );

            migrationBuilder.RenameColumn(
                name: "requester_org_id",
                schema: "marketplace",
                table: "quotes",
                newName: "vendor_org_id"
            );
            migrationBuilder.Sql(
                """
                UPDATE "marketplace"."quotes" SET org_id = vendor_org_id, vendor_org_id = org_id;
                """
            );

            migrationBuilder.AddColumn<Guid>(
                name: "vendor_org_id",
                schema: "marketplace",
                table: "request_events",
                type: "uuid",
                nullable: true
            );
            migrationBuilder.Sql(
                """
                UPDATE "marketplace"."request_events" e SET vendor_org_id = r.vendor_org_id
                FROM "marketplace"."requests" r WHERE r.id = e.request_id;
                DROP POLICY tenant_isolation ON "marketplace"."requests";
                DROP POLICY tenant_isolation ON "marketplace"."request_events";
                DROP POLICY tenant_isolation ON "marketplace"."request_recipients";
                DROP POLICY tenant_isolation ON "marketplace"."quotes";
                DROP POLICY tenant_isolation ON "marketplace"."vendor_profiles";
                DROP POLICY tenant_isolation ON "marketplace"."vendor_credentials";
                CREATE POLICY catalog_read ON "marketplace"."vendor_profiles" FOR SELECT
                    USING (published OR org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);
                CREATE POLICY owner_write ON "marketplace"."vendor_profiles" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);
                CREATE POLICY catalog_read ON "marketplace"."vendor_credentials" FOR SELECT
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR EXISTS (SELECT 1 FROM "marketplace"."vendor_profiles" vp
                                      WHERE vp.org_id = "vendor_credentials".org_id AND vp.published));
                CREATE POLICY owner_write ON "marketplace"."vendor_credentials" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);
                """
            );
            migrationBuilder.EnableRecipientListRls(
                "marketplace",
                "requests",
                recipientsTable: "request_recipients",
                foreignKeyColumn: "request_id",
                recipientOrgColumn: "vendor_org_id",
                counterpartyColumn: "vendor_org_id"
            );
            migrationBuilder.EnableTwoPartyRls("marketplace", "request_events", "vendor_org_id");
            migrationBuilder.EnableTwoPartyRls(
                "marketplace",
                "request_recipients",
                "vendor_org_id"
            );
            migrationBuilder.EnableTwoPartyRls("marketplace", "quotes", "vendor_org_id");

            migrationBuilder.CreateIndex(
                name: "IX_vendor_profiles_published",
                schema: "marketplace",
                table: "vendor_profiles",
                column: "published"
            );

            migrationBuilder.CreateIndex(
                name: "IX_requests_vendor_org_id_status_updated_at",
                schema: "marketplace",
                table: "requests",
                columns: new[] { "vendor_org_id", "status", "updated_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_quotes_request_id_vendor_org_id",
                schema: "marketplace",
                table: "quotes",
                columns: new[] { "request_id", "vendor_org_id" },
                unique: true
            );
        }
    }
}
