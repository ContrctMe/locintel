import { api, ENTITLEMENTS, type EntitlementCode } from '@locintel/api';
import { Card, CardContent, CardHeader, CardTitle } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import {entitlementLabel, fmtDateTime, eventLabel } from '../lib/format';
import { can, useMe } from '../session';


/** The overview (UX review P2): what needs attention, then the plan. */
export function DashboardPage() {
  const { data: me } = useMe();
  const seesSites = can(me, 'sites:read');
  const seesMembers = can(me, 'roles:manage');
  const seesAudit = can(me, 'audit:read');

  const { data: entitlements } = useQuery({
    queryKey: ['entitlements'],
    queryFn: ({ signal }) => api.get('/api/entitlements', { signal }),
  });
  const { data: sites } = useQuery({
    queryKey: ['sites', 'summary'],
    queryFn: ({ signal }) => api.get('/api/sites', { query: { limit: 1 }, signal }),
    enabled: seesSites,
  });
  const { data: invitations } = useQuery({
    queryKey: ['invitations'],
    queryFn: ({ signal }) => api.get('/api/members/invitations', { signal }),
    enabled: seesMembers,
  });
  const { data: events } = useQuery({
    queryKey: ['audit', 'events', 5],
    queryFn: ({ signal }) => api.get('/api/audit/{kind}', { path: { kind: 'events' }, query: { limit: 5 }, signal }),
    enabled: seesAudit,
  });

  if (me?.tier !== 'user') return null;

  const pending = invitations?.filter((i) => i.state === 'pending').length ?? 0;

  return (
    <div className="max-w-3xl space-y-6">
      <h1 className="text-2xl font-semibold">Dashboard</h1>

      <div className="grid grid-cols-2 gap-4">
        {seesSites && (
          <Card>
            <CardContent className="pt-5">
              <Link to="/sites" className="block">
                <div className="text-3xl font-semibold tabular-nums">
                  {sites === undefined ? '—' : sites.total}
                </div>
                <div className="text-sm text-muted-foreground">
                  sites · {sites?.openCount ?? 0} open
                </div>
              </Link>
            </CardContent>
          </Card>
        )}
        {seesMembers && (
          <Card>
            <CardContent className="pt-5">
              <Link to="/members" className="block">
                <div className="text-3xl font-semibold tabular-nums">
                  {invitations === undefined ? '—' : pending}
                </div>
                <div className="text-sm text-muted-foreground">pending invitations</div>
              </Link>
            </CardContent>
          </Card>
        )}
      </div>

      {seesAudit && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between">
              Recent activity
              <Link to="/audit" className="text-sm font-normal text-muted-foreground hover:underline">
                All activity →
              </Link>
            </CardTitle>
          </CardHeader>
          <CardContent>
            {events === undefined ? (
              <p className="text-sm text-muted-foreground">Loading…</p>
            ) : events.length === 0 ? (
              <p className="text-sm text-muted-foreground">Nothing recorded yet.</p>
            ) : (
              <ul className="space-y-1.5 text-sm">
                {events.map((e) => (
                  <li key={e.id} className="flex justify-between gap-4">
                    <span className="min-w-0 truncate">
                      {eventLabel(e.eventName ?? 'unknown')}
                      {e.actorLabel && (
                        <span className="ml-2 text-xs text-muted-foreground">{e.actorLabel}</span>
                      )}
                    </span>
                    <span className="shrink-0 text-xs text-muted-foreground">
                      {fmtDateTime(e.occurredAt)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader><CardTitle>Plan</CardTitle></CardHeader>
        <CardContent>
          <div className="grid grid-cols-1 gap-x-8 gap-y-2 text-sm sm:grid-cols-2">
            {entitlements &&
              (Object.keys(ENTITLEMENTS) as EntitlementCode[]).map((code) => {
                const entry = entitlements[code];
                const limit = Number(entry?.value);
                const showBar =
                  entry?.usage != null && Number.isFinite(limit) && limit > 0;
                const ratio = showBar ? Math.min(Number(entry.usage) / limit, 1) : 0;
                return (
                  <div key={code} className="space-y-1 border-b py-1.5">
                    <div className="flex justify-between">
                      <span className="text-muted-foreground" title={code}>
                        {entitlementLabel(code)}
                      </span>
                      <span className="font-medium tabular-nums">
                        {entry?.usage != null
                          ? `${entry.usage} of ${entry.value}`
                          : entry?.value}
                      </span>
                    </div>
                    {showBar && (
                      <div className="h-1 overflow-hidden rounded-full bg-muted">
                        <div
                          className={ratio >= 1 ? 'h-full bg-destructive' : 'h-full bg-primary'}
                          style={{ width: `${Math.max(ratio * 100, 2)}%` }}
                        />
                      </div>
                    )}
                  </div>
                );
              })}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
