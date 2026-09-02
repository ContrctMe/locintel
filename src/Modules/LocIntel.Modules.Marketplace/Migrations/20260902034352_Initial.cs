using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Marketplace.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "marketplace");

            migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:ltree", ",,");

            migrationBuilder.CreateTable(
                name: "preferred_vendors",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    categories = table.Column<string[]>(type: "text[]", nullable: false),
                    notes = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: true
                    ),
                    blocked = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preferred_vendors", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "request_events",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    body = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    distance_from_site_m = table.Column<double>(
                        type: "double precision",
                        nullable: true
                    ),
                    at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_events", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "requests",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_org_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    path = table.Column<string>(type: "ltree", nullable: false),
                    requester_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
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
                    incident_id = table.Column<Guid>(type: "uuid", nullable: true),
                    case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    submitted_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
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
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requests", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "vendor_credentials",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    label = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    number = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    jurisdiction = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    expires_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_credentials", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "vendor_profiles",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    published = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_vendor_profiles", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_preferred_vendors_org_id_vendor_org_id",
                schema: "marketplace",
                table: "preferred_vendors",
                columns: new[] { "org_id", "vendor_org_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_events_request_id_at",
                schema: "marketplace",
                table: "request_events",
                columns: new[] { "request_id", "at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_requests_org_id_status_updated_at",
                schema: "marketplace",
                table: "requests",
                columns: new[] { "org_id", "status", "updated_at" }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_requests_path",
                    schema: "marketplace",
                    table: "requests",
                    column: "path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_requests_vendor_org_id_status_updated_at",
                schema: "marketplace",
                table: "requests",
                columns: new[] { "vendor_org_id", "status", "updated_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_vendor_credentials_org_id",
                schema: "marketplace",
                table: "vendor_credentials",
                column: "org_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_vendor_profiles_org_id",
                schema: "marketplace",
                table: "vendor_profiles",
                column: "org_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_vendor_profiles_published",
                schema: "marketplace",
                table: "vendor_profiles",
                column: "published"
            );
            // RLS (the new-migration skill checklist). preferred_vendors is the
            // ordinary single-owner shape. The rest are the product's first
            // CROSS-ORG policies, mirrored exactly by the context's "Tenant"
            // filters (blueprint: marketplace tenancy):
            //  - vendor_profiles / vendor_credentials: the owner writes; every
            //    org reads once the profile is published (the catalog)
            //  - requests / request_events: two parties, either side is a
            //    tenant of the row
            migrationBuilder.EnableTenantRls("marketplace", "preferred_vendors");
            migrationBuilder.Sql(
                """
                ALTER TABLE "marketplace"."vendor_profiles" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "marketplace"."vendor_profiles" FORCE ROW LEVEL SECURITY;
                CREATE POLICY catalog_read ON "marketplace"."vendor_profiles" FOR SELECT
                    USING (published OR org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);
                CREATE POLICY owner_write ON "marketplace"."vendor_profiles" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);

                ALTER TABLE "marketplace"."vendor_credentials" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "marketplace"."vendor_credentials" FORCE ROW LEVEL SECURITY;
                CREATE POLICY catalog_read ON "marketplace"."vendor_credentials" FOR SELECT
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR EXISTS (SELECT 1 FROM "marketplace"."vendor_profiles" vp
                                      WHERE vp.org_id = "vendor_credentials".org_id AND vp.published));
                CREATE POLICY owner_write ON "marketplace"."vendor_credentials" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);

                ALTER TABLE "marketplace"."requests" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "marketplace"."requests" FORCE ROW LEVEL SECURITY;
                CREATE POLICY two_party ON "marketplace"."requests" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);

                ALTER TABLE "marketplace"."request_events" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "marketplace"."request_events" FORCE ROW LEVEL SECURITY;
                CREATE POLICY two_party ON "marketplace"."request_events" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "preferred_vendors", schema: "marketplace");

            migrationBuilder.DropTable(name: "request_events", schema: "marketplace");

            migrationBuilder.DropTable(name: "requests", schema: "marketplace");

            migrationBuilder.DropTable(name: "vendor_credentials", schema: "marketplace");

            migrationBuilder.DropTable(name: "vendor_profiles", schema: "marketplace");
        }
    }
}
