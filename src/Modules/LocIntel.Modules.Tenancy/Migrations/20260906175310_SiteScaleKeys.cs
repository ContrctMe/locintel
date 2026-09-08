using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Tenancy.Migrations
{
    /// <summary>
    /// ADR 51: the leakproof keys. Under row security Postgres will not use a
    /// non-leakproof operator as an index condition, which is every PostGIS,
    /// ltree and ILIKE operator - so for the app role the geography and ltree
    /// GiST indexes never served a query. Each hot predicate gets a key a
    /// plain btree comparison can answer: <c>cell</c> (the zoom-20 Morton
    /// tile) for viewports and tiles, <c>path_text</c> ("C" collation) for
    /// subtree scope, <c>site_search_terms</c> for search, and (name, id)
    /// for keyset paging. Backfilled here; TenancyDbContext keeps them in
    /// step on every save from now on.
    /// </summary>
    public partial class SiteScaleKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "cell",
                schema: "tenancy",
                table: "sites",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "path_text",
                schema: "tenancy",
                table: "sites",
                type: "text",
                nullable: false,
                defaultValue: "",
                collation: "C"
            );

            migrationBuilder.CreateTable(
                name: "site_search_terms",
                schema: "tenancy",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false,
                        collation: "C"
                    ),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_search_terms", x => new { x.site_id, x.term });
                    table.ForeignKey(
                        name: "FK_site_search_terms_sites_site_id",
                        column: x => x.site_id,
                        principalSchema: "tenancy",
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            // one owning org per row (ADR 48); the policy is the same as every tenant table
            migrationBuilder.EnableTenantRls("tenancy", "site_search_terms");

            // Backfill before the indexes exist, so they build once. The cell
            // function is the same arithmetic as SpatialCells.Key (Web Mercator
            // tile at zoom 20, bits interleaved) and is dropped again below:
            // from here on the keys are written by the application.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION tenancy.__morton_spread(v bigint) RETURNS bigint LANGUAGE sql IMMUTABLE AS $$
                    SELECT v5 FROM (SELECT v & 4294967295 AS v0) a,
                        LATERAL (SELECT (v0 | (v0 << 16)) & 281470681808895 AS v1) b,
                        LATERAL (SELECT (v1 | (v1 << 8)) & 71777214294589695 AS v2) c,
                        LATERAL (SELECT (v2 | (v2 << 4)) & 1085102592571150095 AS v3) d,
                        LATERAL (SELECT (v3 | (v3 << 2)) & 3689348814741910323 AS v4) e,
                        LATERAL (SELECT (v4 | (v4 << 1)) & 6148914691236517205 AS v5) f
                $$;
                CREATE FUNCTION tenancy.__spatial_cell(lat double precision, lon double precision) RETURNS bigint LANGUAGE sql IMMUTABLE AS $$
                    SELECT tenancy.__morton_spread(x) | (tenancy.__morton_spread(y) << 1)
                    FROM (
                        SELECT least(greatest(floor((lon + 180) / 360 * 1048576), 0), 1048575)::bigint AS x,
                               least(greatest(floor((1 - ln(tan(radians(c)) + 1 / cos(radians(c))) / pi()) / 2 * 1048576), 0), 1048575)::bigint AS y
                        FROM (SELECT least(greatest(lat, -85.05112878), 85.05112878) AS c) clamped
                    ) t
                $$;

                UPDATE tenancy.sites
                SET path_text = path::text,
                    cell = CASE WHEN latitude IS NOT NULL AND longitude IS NOT NULL
                                THEN tenancy.__spatial_cell(latitude, longitude) END;

                INSERT INTO tenancy.site_search_terms (org_id, site_id, term, name)
                SELECT DISTINCT s.org_id, s.id, left(w, 120), s.name
                FROM tenancy.sites s,
                     LATERAL regexp_split_to_table(lower(s.name || ' ' || coalesce(s.city, '')), '[^[:alnum:]]+') AS w
                WHERE w <> '';

                DROP FUNCTION tenancy.__spatial_cell(double precision, double precision);
                DROP FUNCTION tenancy.__morton_spread(bigint);
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_sites_org_id_cell",
                schema: "tenancy",
                table: "sites",
                columns: new[] { "org_id", "cell" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_sites_org_id_name_id",
                schema: "tenancy",
                table: "sites",
                columns: new[] { "org_id", "name", "id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_sites_org_id_path_text",
                schema: "tenancy",
                table: "sites",
                columns: new[] { "org_id", "path_text" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_sites_org_id_status",
                schema: "tenancy",
                table: "sites",
                columns: new[] { "org_id", "status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_site_search_terms_org_id_term_name_site_id",
                schema: "tenancy",
                table: "site_search_terms",
                columns: new[] { "org_id", "term", "name", "site_id" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the term table's policy goes with the table; nothing else references it
            migrationBuilder.DropTable(name: "site_search_terms", schema: "tenancy");

            migrationBuilder.DropIndex(
                name: "IX_sites_org_id_cell",
                schema: "tenancy",
                table: "sites"
            );

            migrationBuilder.DropIndex(
                name: "IX_sites_org_id_name_id",
                schema: "tenancy",
                table: "sites"
            );

            migrationBuilder.DropIndex(
                name: "IX_sites_org_id_path_text",
                schema: "tenancy",
                table: "sites"
            );

            migrationBuilder.DropIndex(
                name: "IX_sites_org_id_status",
                schema: "tenancy",
                table: "sites"
            );

            migrationBuilder.DropColumn(name: "cell", schema: "tenancy", table: "sites");

            migrationBuilder.DropColumn(name: "path_text", schema: "tenancy", table: "sites");
        }
    }
}
