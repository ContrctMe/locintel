using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddCapacityReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capacity_reservations",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capacity_reservations", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_capacity_reservations_org_id_batch_id",
                schema: "platform",
                table: "capacity_reservations",
                columns: new[] { "org_id", "batch_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_capacity_reservations_org_id_code",
                schema: "platform",
                table: "capacity_reservations",
                columns: new[] { "org_id", "code" }
            );
            LocIntel.Platform.Data.RlsMigrationExtensions.EnableTenantRls(
                migrationBuilder,
                "platform",
                "capacity_reservations"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "capacity_reservations", schema: "platform");
        }
    }
}
