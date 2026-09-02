using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Audit.Migrations
{
    /// <summary>
    /// Security review: RLS on a partitioned parent is enforced when querying
    /// THROUGH the parent (the app's only path), but the partition children
    /// carried no RLS of their own - so a direct-partition read would bypass
    /// tenant isolation. Belt-and-suspenders: every partition gets its own
    /// ENABLE + FORCE + tenant policy, and the ensure() function stamps the
    /// same onto every partition it creates from now on.
    /// </summary>
    public partial class ForceRlsOnAccessLogPartitions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // retrofit existing partitions (default + any monthly)
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE r record;
                BEGIN
                    FOR r IN
                        SELECT c.relname
                        FROM pg_inherits i
                        JOIN pg_class c ON c.oid = i.inhrelid
                        JOIN pg_class p ON p.oid = i.inhparent
                        JOIN pg_namespace n ON n.oid = p.relnamespace
                        WHERE n.nspname = 'audit' AND p.relname = 'access_log'
                    LOOP
                        EXECUTE format(
                            $f$ALTER TABLE audit.%1$I ENABLE ROW LEVEL SECURITY;
                               ALTER TABLE audit.%1$I FORCE ROW LEVEL SECURITY;
                               DROP POLICY IF EXISTS tenant_isolation ON audit.%1$I;
                               CREATE POLICY tenant_isolation ON audit.%1$I
                                   USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                                   WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);$f$,
                            r.relname
                        );
                    END LOOP;
                END $$;
                """
            );

            // and every future partition, stamped by ensure()
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
                            EXECUTE format(
                                $f2$ALTER TABLE audit.%1$I ENABLE ROW LEVEL SECURITY;
                                    ALTER TABLE audit.%1$I FORCE ROW LEVEL SECURITY;
                                    CREATE POLICY tenant_isolation ON audit.%1$I
                                        USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                                        WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);$f2$,
                                part
                            );
                        END IF;
                    END LOOP;
                END $fn$;

                SELECT audit.ensure_access_log_partitions();
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down leaves the partition policies in place (dropping tenant
            // isolation is never the safe direction); the ensure() function
            // reverts to the pre-policy body.
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
                """
            );
        }
    }
}
