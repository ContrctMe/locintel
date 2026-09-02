using Microsoft.EntityFrameworkCore;

namespace LocIntel.Api;

/// <summary>
/// The migrate role (ADR 38): migrations never run on api/worker boot. This
/// role connects as the database OWNER, applies every module's migrations,
/// provisions the unprivileged app role the api and worker connect as, and
/// exits. The split is load-bearing: superusers bypass RLS unconditionally,
/// so an api holding owner credentials would have every tenant-isolation
/// policy silently inert - and the app role holds no DDL beyond schema
/// creation (Wolverine owns its envelope schema), so policies and grants are
/// beyond application code's reach.
/// </summary>
public sealed class MigrationRunner(
    IServiceProvider services,
    IHostApplicationLifetime lifetime,
    IConfiguration configuration,
    ILogger<MigrationRunner> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RunAsync(stoppingToken);
                logger.LogInformation("migrations applied; app role provisioned");
                lifetime.StopApplication();
                return;
            }
            catch (Exception e) when (attempt < 30 && !stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("migrate waiting on the database ({Error})", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<LocIntel.Modules.Tenancy.Data.TenancyDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<LocIntel.Modules.Identity.Data.IdentityDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<LocIntel.Modules.Entitlements.Data.EntitlementsDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<LocIntel.Modules.Audit.Data.AuditDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<LocIntel.Modules.Storage.Data.StorageDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<LocIntel.Modules.Checklists.Data.ChecklistsDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<LocIntel.Modules.Ingest.Data.IngestDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<LocIntel.Modules.Incidents.Data.IncidentsDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<LocIntel.Modules.Entities.Data.EntitiesDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<LocIntel.Modules.Cases.Data.CasesDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<LocIntel.Platform.Infra.PlatformDbContext>()
            .Database.MigrateAsync(ct);

        // App role provisioning is idempotent and re-runs every migrate, so
        // grants always cover tables the latest migrations just created.
        var password = configuration["Database:AppPassword"] ?? "app_user";
        var db = sp.GetRequiredService<LocIntel.Modules.Tenancy.Data.TenancyDbContext>();
        // one transaction so the parameterized password (a transaction-local
        // GUC - no string splicing into DDL) and the DO block share a session
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync(
            $"SELECT set_config('locintel.app_password', {password}, true)",
            ct
        );
        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'app_user') THEN
                    CREATE ROLE app_user LOGIN NOSUPERUSER;
                END IF;
                EXECUTE format('ALTER ROLE app_user PASSWORD %L', current_setting('locintel.app_password'));
                -- Wolverine owns its envelope schema; the app creates it at startup
                EXECUTE format('GRANT CREATE ON DATABASE %I TO app_user', current_database());
                -- a wolverine schema created by an OWNER-connected app (pre-split
                -- volumes) must be handed to the app role, or its migrations fail
                IF EXISTS (SELECT FROM pg_namespace WHERE nspname = 'wolverine') THEN
                    EXECUTE 'ALTER SCHEMA wolverine OWNER TO app_user';
                    DECLARE r record;
                    BEGIN
                        FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = 'wolverine' LOOP
                            EXECUTE format('ALTER TABLE wolverine.%I OWNER TO app_user', r.tablename);
                        END LOOP;
                        FOR r IN SELECT sequencename FROM pg_sequences WHERE schemaname = 'wolverine' LOOP
                            EXECUTE format('ALTER SEQUENCE wolverine.%I OWNER TO app_user', r.sequencename);
                        END LOOP;
                        FOR r IN
                            SELECT p.proname, pg_get_function_identity_arguments(p.oid) AS args
                            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                            WHERE n.nspname = 'wolverine'
                        LOOP
                            EXECUTE format('ALTER FUNCTION wolverine.%I(%s) OWNER TO app_user', r.proname, r.args);
                        END LOOP;
                    END;
                END IF;
            END $$;
            GRANT USAGE ON SCHEMA tenancy, identity, entitlements, audit, storage, platform, ingest, checklists, incidents, entities, cases TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tenancy TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA identity TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA entitlements TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA audit TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA storage TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA platform TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA ingest TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA checklists TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA incidents TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA entities TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA cases TO app_user;
            """,
            ct
        );
        await tx.CommitAsync(ct);
    }
}
