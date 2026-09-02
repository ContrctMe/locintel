using Microsoft.EntityFrameworkCore.Migrations;
using LocIntel.Platform.Data;

#nullable disable

namespace LocIntel.Modules.Audit.Migrations
{
    /// <inheritdoc />
    public partial class PartitionAccessLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Native monthly partitioning (ADR 38 follow-up): the access log
            // is the highest-volume table in the system, and range-partitioning
            // by occurred_at makes retention a partition DROP instead of a
            // mass DELETE. Postgres cannot convert a table in place, so:
            // rename, recreate partitioned, copy, drop.
            migrationBuilder.Sql(
                """
                ALTER TABLE audit.access_log RENAME TO access_log_unpartitioned;
                ALTER INDEX audit."PK_access_log" RENAME TO "PK_access_log_unpartitioned";
                ALTER INDEX audit."IX_access_log_org_id_occurred_at" RENAME TO "IX_access_log_old";

                CREATE TABLE audit.access_log (
                    id uuid NOT NULL,
                    org_id uuid NOT NULL,
                    actor_tier character varying(20) NOT NULL,
                    actor_id uuid,
                    method character varying(10) NOT NULL,
                    path character varying(500) NOT NULL,
                    status_code integer NOT NULL,
                    occurred_at timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_access_log" PRIMARY KEY (id, occurred_at)
                ) PARTITION BY RANGE (occurred_at);
                CREATE INDEX "IX_access_log_org_id_occurred_at"
                    ON audit.access_log (org_id, occurred_at);

                -- catch-all for rows outside any monthly partition (e.g. the
                -- copied history below); never dropped by pruning
                CREATE TABLE audit.access_log_default
                    PARTITION OF audit.access_log DEFAULT;

                INSERT INTO audit.access_log
                    SELECT * FROM audit.access_log_unpartitioned;
                DROP TABLE audit.access_log_unpartitioned;
                """
            );
            migrationBuilder.EnableTenantRls("audit", "access_log");

            // Partition maintenance is DDL, and the app role holds none (ADR
            // 38): SECURITY DEFINER functions owned by the migrator are the
            // app's only door. ensure() keeps current+next month present;
            // prune() drops WHOLE monthly partitions older than keep_days
            // (per-org entitlement retention stays row-level in PurgeAuditData;
            // pruning is the coarse floor under it).
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION audit.ensure_access_log_partitions()
                RETURNS void LANGUAGE plpgsql SECURITY DEFINER
                SET search_path = audit, pg_catalog AS $fn$
                DECLARE
                    start_d date; end_d date; part text;
                BEGIN
                    FOR i IN 0..1 LOOP
                        start_d := (date_trunc('month', now()) + make_interval(months => i))::date;
                        end_d := (date_trunc('month', now()) + make_interval(months => i + 1))::date;
                        part := 'access_log_y' || to_char(start_d, 'YYYY') || 'm' || to_char(start_d, 'MM');
                        IF NOT EXISTS (
                            SELECT FROM pg_class c
                            JOIN pg_namespace n ON n.oid = c.relnamespace
                            WHERE n.nspname = 'audit' AND c.relname = part
                        ) THEN
                            EXECUTE format(
                                'CREATE TABLE audit.%I PARTITION OF audit.access_log FOR VALUES FROM (%L) TO (%L)',
                                part, start_d, end_d
                            );
                        END IF;
                    END LOOP;
                END $fn$;

                CREATE OR REPLACE FUNCTION audit.prune_access_log_partitions(keep_days int)
                RETURNS int LANGUAGE plpgsql SECURITY DEFINER
                SET search_path = audit, pg_catalog AS $fn$
                DECLARE
                    r record; y int; mo int; dropped int := 0;
                BEGIN
                    FOR r IN
                        SELECT c.relname
                        FROM pg_inherits i
                        JOIN pg_class c ON c.oid = i.inhrelid
                        JOIN pg_class p ON p.oid = i.inhparent
                        JOIN pg_namespace n ON n.oid = p.relnamespace
                        WHERE n.nspname = 'audit' AND p.relname = 'access_log'
                          AND c.relname ~ '^access_log_y\d{4}m\d{2}$'
                    LOOP
                        y := substring(r.relname FROM 'y(\d{4})m')::int;
                        mo := substring(r.relname FROM 'm(\d{2})$')::int;
                        IF make_date(y, mo, 1) + interval '1 month'
                           < now() - make_interval(days => keep_days) THEN
                            EXECUTE format('DROP TABLE audit.%I', r.relname);
                            dropped := dropped + 1;
                        END IF;
                    END LOOP;
                    RETURN dropped;
                END $fn$;

                SELECT audit.ensure_access_log_partitions();
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION audit.prune_access_log_partitions(int);
                DROP FUNCTION audit.ensure_access_log_partitions();

                ALTER TABLE audit.access_log RENAME TO access_log_partitioned;
                ALTER INDEX audit."PK_access_log" RENAME TO "PK_access_log_partitioned";
                ALTER INDEX audit."IX_access_log_org_id_occurred_at" RENAME TO "IX_access_log_part";

                CREATE TABLE audit.access_log (
                    id uuid NOT NULL,
                    org_id uuid NOT NULL,
                    actor_tier character varying(20) NOT NULL,
                    actor_id uuid,
                    method character varying(10) NOT NULL,
                    path character varying(500) NOT NULL,
                    status_code integer NOT NULL,
                    occurred_at timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_access_log" PRIMARY KEY (id)
                );
                CREATE INDEX "IX_access_log_org_id_occurred_at"
                    ON audit.access_log (org_id, occurred_at);

                INSERT INTO audit.access_log
                    SELECT * FROM audit.access_log_partitioned;
                DROP TABLE audit.access_log_partitioned;
                """
            );
            migrationBuilder.EnableTenantRls("audit", "access_log");
        }
    }
}
