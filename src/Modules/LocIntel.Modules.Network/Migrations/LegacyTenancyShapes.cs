using Microsoft.EntityFrameworkCore.Migrations;

namespace LocIntel.Modules.Network.Migrations;

/// <summary>
/// The SQL the Platform tenancy-shape helpers emitted when the applied
/// migrations in this folder were written, frozen here so those migrations
/// keep compiling and re-applying byte-for-byte after ADR 48 removed the
/// helpers. Applied migrations are immutable; new tables use EnableTenantRls
/// and a later migration drops these policies. Never call these from new
/// migrations.
/// </summary>
internal static class LegacyTenancyShapes
{
    private const string Current = "NULLIF(current_setting('app.org_id', true), '')::uuid";

    public static void EnableTwoPartyRls(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        string counterpartyColumn,
        string orgColumn = "org_id"
    ) =>
        migrationBuilder.Sql(
            $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "{schema}"."{table}"
                USING (
                    "{orgColumn}" = {Current}
                    OR "{counterpartyColumn}" = {Current}
                )
                WITH CHECK (
                    "{orgColumn}" = {Current}
                    OR "{counterpartyColumn}" = {Current}
                );
            """
        );

    public static void EnableRecipientListRls(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        string recipientsTable,
        string foreignKeyColumn,
        string recipientOrgColumn,
        string? counterpartyColumn = null,
        string orgColumn = "org_id",
        string? recipientPredicate = null,
        bool writableByRecipient = false
    )
    {
        var owner = $"\"{orgColumn}\" = {Current}";
        var party = counterpartyColumn is null
            ? owner
            : $"{owner} OR \"{counterpartyColumn}\" = {Current}";
        var gate = recipientPredicate is null ? "" : $" AND ({recipientPredicate})";
        var onTheList =
            $"EXISTS (SELECT 1 FROM \"{schema}\".\"{recipientsTable}\" r "
            + $"WHERE r.\"{foreignKeyColumn}\" = \"{table}\".id "
            + $"AND r.\"{recipientOrgColumn}\" = {Current}{gate})";
        var writable = writableByRecipient ? $"{party} OR {onTheList}" : party;
        migrationBuilder.Sql(
            $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "{schema}"."{table}"
                USING ({party} OR {onTheList})
                WITH CHECK ({writable});
            """
        );
    }
}
