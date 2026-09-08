-- Read-only migration inventory. Run with a database identity able to read
-- across forced RLS: psql "$DATABASE_URL" -q -f export-legacy-policy.sql > legacy-rates.csv
-- row_security=off fails if that privilege is missing; never export a silently
-- tenant-filtered subset. This export does not modify the source tables.
-- Run before upgrading: later billing synchronization can remove retired plan rows.
BEGIN;
SET LOCAL row_security = off;
COPY (
    SELECT o.id AS org_id, o.slug,
           coalesce(e.value, a.value, '600') AS effective_requests_per_minute,
           coalesce(a.source, 'legacy-catalog-default') AS assigned_source,
           e.expires_at AS exception_expires_at,
           e.reason AS exception_reason
    FROM tenancy.organizations o
    LEFT JOIN entitlements.org_entitlements a
      ON a.org_id = o.id AND a.code = 'api.requests_per_minute'
    LEFT JOIN LATERAL (
        SELECT value, expires_at, reason
        FROM entitlements.entitlement_exceptions
        WHERE org_id = o.id AND code = 'api.requests_per_minute'
          AND expires_at > now()
        ORDER BY created_at DESC
        LIMIT 1
    ) e ON true
    ORDER BY o.id
) TO STDOUT WITH (FORMAT csv, HEADER true);
ROLLBACK;
