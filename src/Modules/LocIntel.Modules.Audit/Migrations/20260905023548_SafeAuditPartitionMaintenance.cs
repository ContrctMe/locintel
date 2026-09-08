using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Audit.Migrations
{
    /// <inheritdoc />
    public partial class SafeAuditPartitionMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These are the existing tenant-owned access rows, not a new global
            // data model. Definer operations retain FORCE RLS on every partition.
            // row_security=off fails closed if the migrator cannot see all rows;
            // a filtered view must never be mistaken for an empty partition.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION audit.ensure_access_log_partitions()
                RETURNS void LANGUAGE plpgsql SECURITY DEFINER
                SET search_path = audit, pg_catalog SET row_security = off AS $fn$
                DECLARE start_d date; end_d date; part text;
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('audit.partition-maintenance', 0));
                    FOR i IN 0..1 LOOP
                        start_d := (date_trunc('month', now()) + make_interval(months => i))::date;
                        end_d := (date_trunc('month', now()) + make_interval(months => i + 1))::date;
                        part := 'access_log_y' || to_char(start_d, 'YYYY') || 'm' || to_char(start_d, 'MM');
                        IF to_regclass('audit.' || part) IS NULL THEN
                            -- ponytail: exclusive parent lock only for missing months;
                            -- online backfill is an upgrade if measured downtime requires it.
                            LOCK TABLE audit.access_log IN ACCESS EXCLUSIVE MODE;
                            EXECUTE format('CREATE TABLE audit.%I (LIKE audit.access_log INCLUDING ALL)', part);
                            EXECUTE format(
                                'WITH moved AS (DELETE FROM audit.access_log_default WHERE occurred_at >= %L AND occurred_at < %L RETURNING *) INSERT INTO audit.%I SELECT * FROM moved',
                                start_d, end_d, part);
                            EXECUTE format(
                                'ALTER TABLE audit.access_log ATTACH PARTITION audit.%I FOR VALUES FROM (%L) TO (%L)',
                                part, start_d, end_d);
                            EXECUTE format(
                                $rls$ALTER TABLE audit.%1$I ENABLE ROW LEVEL SECURITY;
                                    ALTER TABLE audit.%1$I FORCE ROW LEVEL SECURITY;
                                    CREATE POLICY tenant_isolation ON audit.%1$I
                                        USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                                        WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);$rls$,
                                part);
                        END IF;
                    END LOOP;
                END $fn$;

                CREATE OR REPLACE FUNCTION audit.prune_access_log_partitions(keep_days int)
                RETURNS int LANGUAGE plpgsql SECURITY DEFINER
                SET search_path = audit, pg_catalog SET row_security = off AS $fn$
                DECLARE r record; y int; mo int; dropped int := 0; populated boolean;
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('audit.partition-maintenance', 0));
                    FOR r IN
                        SELECT c.relname FROM pg_inherits i
                        JOIN pg_class c ON c.oid = i.inhrelid
                        JOIN pg_class p ON p.oid = i.inhparent
                        JOIN pg_namespace n ON n.oid = p.relnamespace
                        WHERE n.nspname = 'audit' AND p.relname = 'access_log'
                          AND c.relname ~ '^access_log_y\d{4}m\d{2}$'
                        ORDER BY c.relname
                    LOOP
                        y := substring(r.relname FROM 'y(\d{4})m')::int;
                        mo := substring(r.relname FROM 'm(\d{2})$')::int;
                        IF make_date(y, mo, 1) + interval '1 month' < now() - make_interval(days => keep_days) THEN
                            -- Lock parent first, matching ensure/normal partition DDL lock order.
                            LOCK TABLE audit.access_log IN ACCESS EXCLUSIVE MODE;
                            EXECUTE format('SELECT EXISTS (SELECT 1 FROM audit.%I)', r.relname) INTO populated;
                            IF NOT populated THEN
                                EXECUTE format('DROP TABLE audit.%I', r.relname);
                                dropped := dropped + 1;
                            END IF;
                        END IF;
                    END LOOP;
                    RETURN dropped;
                END $fn$;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the previous function definitions; table contents and RLS
            // remain intact. No applied migration or frozen helper is changed.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION audit.ensure_access_log_partitions()
                RETURNS void LANGUAGE plpgsql SECURITY DEFINER
                SET search_path = audit, pg_catalog AS $fn$
                DECLARE start_d date; end_d date; part text;
                BEGIN
                    FOR i IN 0..1 LOOP
                        start_d := (date_trunc('month', now()) + make_interval(months => i))::date;
                        end_d := (date_trunc('month', now()) + make_interval(months => i + 1))::date;
                        part := 'access_log_y' || to_char(start_d, 'YYYY') || 'm' || to_char(start_d, 'MM');
                        IF NOT EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                                       WHERE n.nspname = 'audit' AND c.relname = part) THEN
                            EXECUTE format('CREATE TABLE audit.%I PARTITION OF audit.access_log FOR VALUES FROM (%L) TO (%L)', part, start_d, end_d);
                            EXECUTE format(
                                $rls$ALTER TABLE audit.%1$I ENABLE ROW LEVEL SECURITY;
                                    ALTER TABLE audit.%1$I FORCE ROW LEVEL SECURITY;
                                    CREATE POLICY tenant_isolation ON audit.%1$I
                                        USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                                        WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);$rls$, part);
                        END IF;
                    END LOOP;
                END $fn$;
                CREATE OR REPLACE FUNCTION audit.prune_access_log_partitions(keep_days int)
                RETURNS int LANGUAGE plpgsql SECURITY DEFINER
                SET search_path = audit, pg_catalog AS $fn$
                DECLARE r record; y int; mo int; dropped int := 0;
                BEGIN
                    FOR r IN
                        SELECT c.relname FROM pg_inherits i
                        JOIN pg_class c ON c.oid = i.inhrelid
                        JOIN pg_class p ON p.oid = i.inhparent
                        JOIN pg_namespace n ON n.oid = p.relnamespace
                        WHERE n.nspname = 'audit' AND p.relname = 'access_log'
                          AND c.relname ~ '^access_log_y\d{4}m\d{2}$'
                    LOOP
                        y := substring(r.relname FROM 'y(\d{4})m')::int;
                        mo := substring(r.relname FROM 'm(\d{2})$')::int;
                        IF make_date(y, mo, 1) + interval '1 month' < now() - make_interval(days => keep_days) THEN
                            EXECUTE format('DROP TABLE audit.%I', r.relname);
                            dropped := dropped + 1;
                        END IF;
                    END LOOP;
                    RETURN dropped;
                END $fn$;
                """
            );
        }
    }
}
