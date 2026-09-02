using LocIntel.Platform.Data;
using Npgsql;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The two cross-org tenancy shapes, proven against a real Postgres rather
/// than reviewed by eye. A fork hand-wrote these policies four times; a
/// hand-rolled cross-org policy is exactly where a tenant-isolation bug
/// hides, so the shared helpers get adversarial tests: who can read, who
/// can write, and who can forge a row naming orgs they are not.
/// </summary>
public class TenancyShapeRlsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly Guid OrgOwner = Guid.NewGuid();
    private static readonly Guid OrgCounterparty = Guid.NewGuid();
    private static readonly Guid OrgStranger = Guid.NewGuid();

    private async Task<NpgsqlConnection> AsOrg(Guid org)
    {
        var conn = new NpgsqlConnection(fixture.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT set_config('app.org_id', $1, false)", conn);
        cmd.Parameters.AddWithValue(org.ToString());
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

    private async Task SetupAsync(string table, string columns, string policySql)
    {
        await using var admin = new NpgsqlConnection(fixture.PostgresConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"""
            DROP TABLE IF EXISTS platform.{table};
            CREATE TABLE platform.{table} ({columns});
            GRANT SELECT, INSERT, UPDATE, DELETE ON platform.{table} TO app_user;
            {policySql}
            """,
            admin
        );
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAsync(NpgsqlConnection conn, string table)
    {
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM platform.{table}", conn);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Two_party_rows_are_visible_to_both_sides_and_nobody_else()
    {
        await SetupAsync(
            "two_party_probe",
            "id uuid primary key, org_id uuid not null, counterparty_org_id uuid",
            RlsMigrationExtensions.TwoPartySql("platform", "two_party_probe", "counterparty_org_id")
        );

        await using (var owner = await AsOrg(OrgOwner))
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO platform.two_party_probe VALUES (gen_random_uuid(), $1, $2)",
                owner
            );
            insert.Parameters.AddWithValue(OrgOwner);
            insert.Parameters.AddWithValue(OrgCounterparty);
            await insert.ExecuteNonQueryAsync();
            Assert.Equal(1, await CountAsync(owner, "two_party_probe"));
        }

        await using (var counterparty = await AsOrg(OrgCounterparty))
            Assert.Equal(1, await CountAsync(counterparty, "two_party_probe"));

        await using (var stranger = await AsOrg(OrgStranger))
            Assert.Equal(0, await CountAsync(stranger, "two_party_probe"));

        // WITH CHECK: a tenant must not forge a row between two OTHER orgs
        await using (var stranger = await AsOrg(OrgStranger))
        {
            await using var forge = new NpgsqlCommand(
                "INSERT INTO platform.two_party_probe VALUES (gen_random_uuid(), $1, $2)",
                stranger
            );
            forge.Parameters.AddWithValue(OrgOwner);
            forge.Parameters.AddWithValue(OrgCounterparty);
            await Assert.ThrowsAsync<PostgresException>(() => forge.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task Published_catalog_rows_read_widely_but_write_only_for_the_owner()
    {
        await SetupAsync(
            "catalog_probe",
            "id uuid primary key, org_id uuid not null, published boolean not null",
            RlsMigrationExtensions.CatalogSql("platform", "catalog_probe")
        );

        await using (var owner = await AsOrg(OrgOwner))
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO platform.catalog_probe VALUES (gen_random_uuid(), $1, true), "
                    + "(gen_random_uuid(), $1, false)",
                owner
            );
            insert.Parameters.AddWithValue(OrgOwner);
            await insert.ExecuteNonQueryAsync();
            Assert.Equal(2, await CountAsync(owner, "catalog_probe")); // both, published or not
        }

        await using (var stranger = await AsOrg(OrgStranger))
        {
            // the published one only - the draft stays private
            Assert.Equal(1, await CountAsync(stranger, "catalog_probe"));

            // reading is not writing: a stranger cannot edit a published row
            await using var hijack = new NpgsqlCommand(
                "UPDATE platform.catalog_probe SET published = false",
                stranger
            );
            Assert.Equal(0, await hijack.ExecuteNonQueryAsync());

            // nor plant a row under someone else's org
            await using var forge = new NpgsqlCommand(
                "INSERT INTO platform.catalog_probe VALUES (gen_random_uuid(), $1, true)",
                stranger
            );
            forge.Parameters.AddWithValue(OrgOwner);
            await Assert.ThrowsAsync<PostgresException>(() => forge.ExecuteNonQueryAsync());
        }
    }
}
