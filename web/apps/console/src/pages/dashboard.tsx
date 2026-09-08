import type { ReactNode } from 'react';
import { api, ENTITLEMENTS, type EntitlementCode } from '@locintel/api';
import { useQuery } from '@tanstack/react-query';
import { Card, CardContent, Badge, buttonVariants, IconTile, Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle, Progress } from '@locintel/ui';
import { Link } from '@tanstack/react-router';
import { Activity, Check, Circle, MapPin, Users } from 'lucide-react';
import { EmptyState, Loading, PageHeader, Panel, Stat } from '../components/page';
import { useHierarchy } from '../features/sites/hooks';
import {entitlementLabel, fmtDateTime, eventLabel } from '../lib/format';
import { can, useMe } from '../session';

/** The levels every org is born with; naming them is the first setup step. */
const DEFAULT_LEVELS = ['Region', 'Market'];

/**
 * A new org's first few steps (flow review, 2026-09): the dashboard says
 * what to do next instead of showing zeros. Each row is a fact the org's
 * data answers; once every row is done the list is gone for good.
 */
export type SetupFacts = {
  levels: string[];
  nodeCount: number;
  siteCount: number;
  memberCount: number | undefined;
  pendingInvites: number;
  manageHierarchy: boolean;
  manageSites: boolean;
  manageMembers: boolean;
};

/** The steps as facts decide them: what is done, what is next, and where each leads. */
export function setupSteps({
  levels,
  nodeCount,
  siteCount,
  memberCount,
  pendingInvites,
  manageHierarchy,
  manageSites,
  manageMembers,
}: SetupFacts) {
  return [
    manageHierarchy && {
      key: 'levels',
      title: 'Name your levels',
      description: `Regions and markets, districts and stores - whatever you call the layers between the organization and a site. Today: ${levels.join(' → ')}.`,
      done: levels.join('|') !== DEFAULT_LEVELS.join('|') || nodeCount > 1,
      to: '/hierarchy',
      action: 'Rename levels',
    },
    manageHierarchy && {
      key: 'node',
      title: 'Add your first node',
      description: 'A region, a district, a market: the branch your first sites hang from. Sites can also sit on the root.',
      done: nodeCount > 1,
      to: '/hierarchy',
      action: 'Add a node',
    },
    manageSites && {
      key: 'site',
      title: 'Add your first site',
      description: 'A name, a time zone, and where it sits. Address and coordinates put it on the map.',
      done: siteCount > 0,
      to: '/sites',
      action: 'Add a site',
    },
    manageMembers && {
      key: 'invite',
      title: 'Invite your team',
      description: 'Site managers see their site\'s checklists; regional managers run a subtree; admins run the org.',
      done: (memberCount ?? 0) > 1 || pendingInvites > 0,
      to: '/members',
      action: 'Invite someone',
    },
  ].filter((step): step is Exclude<typeof step, false> => step !== false);
}

function SetupList(facts: SetupFacts) {
  const steps = setupSteps(facts);
  if (steps.every((s) => s.done)) return null;
  const remaining = steps.filter((s) => !s.done).length;
  return (
    <Panel
      title="Set up your organization"
      description={`${remaining} step${remaining === 1 ? '' : 's'} left. Nothing here is required; it is the order most teams take.`}
      flush
    >
      <ItemGroup className="divide-y">
        {steps.map((step) => (
          <Item key={step.key} size="sm" className="rounded-none" role="listitem">
            <ItemMedia variant="icon" className={step.done ? 'text-success' : 'text-muted-foreground'}>
              {step.done ? <Check aria-label="Done" /> : <Circle aria-label="To do" />}
            </ItemMedia>
            <ItemContent>
              <ItemTitle className={step.done ? 'text-muted-foreground line-through' : undefined}>{step.title}</ItemTitle>
              {!step.done && <ItemDescription>{step.description}</ItemDescription>}
            </ItemContent>
            {!step.done && (
              <ItemActions>
                <Link to={step.to} className={buttonVariants({ variant: 'outline', size: 'sm' })}>
                  {step.action}
                </Link>
              </ItemActions>
            )}
          </Item>
        ))}
      </ItemGroup>
    </Panel>
  );
}


/** A plan value the way a person reads it: "20,006", not "20006"; "On", not "true". */
const planNumber = (v: unknown) => {
  const n = Number(v);
  return Number.isFinite(n) ? n.toLocaleString() : String(v ?? '');
};
const planValue = (v: unknown): ReactNode => {
  if (v === 'true' || v === true) return <Badge variant="success-light">On</Badge>;
  if (v === 'false' || v === false) return <Badge variant="secondary">Off</Badge>;
  return planNumber(v);
};


/** The overview (UX review P2): what needs attention, then the plan. */
export function DashboardPage() {
  const { data: me } = useMe();
  const seesSites = can(me, 'sites:read');
  const seesMembers = can(me, 'roles:manage');
  const seesAudit = can(me, 'audit:read');
  const seesIncidents = can(me, 'incidents:read');
  const seesAlerts = can(me, 'alerts:read');
  const manageHierarchy = can(me, 'hierarchy:manage');
  const manageSites = can(me, 'sites:manage');

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
  const { data: incidents } = useQuery({
    queryKey: ['incidents', 'stats', 'dashboard'],
    queryFn: ({ signal }) => api.get('/api/incidents/stats', { signal }),
    enabled: seesIncidents,
  });
  const { data: alerts } = useQuery({
    queryKey: ['alerts', 'summary'],
    queryFn: ({ signal }) => api.get('/api/alerts/summary', { signal }),
    enabled: seesAlerts,
  });
  // the setup list's facts: the tree and the member count (roles:manage
  // already reads the invitations; members is the same page's other read)
  const { data: hierarchy } = useHierarchy();
  const { data: members } = useQuery({
    queryKey: ['members', 'count'],
    queryFn: ({ signal }) => api.get('/api/members', { query: { limit: 1 }, signal }),
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
    <div className="max-w-4xl space-y-6">
      <PageHeader title="Dashboard" description="What needs attention, then the plan." />


      <div className="grid grid-cols-2 gap-4">
        {seesIncidents && (
          <Card>
            <CardContent className="pt-5">
              <Link to="/incidents" className="block">
                <div className="text-3xl font-semibold tabular-nums">
                  {incidents === undefined ? '—' : incidents.total}
                </div>
                <div className="text-sm text-muted-foreground">
                  incidents, last 30 days · {incidents?.open ?? 0} open
                  {incidents && Number(incidents.totalLoss) > 0 && ` · $${Number(incidents.totalLoss).toLocaleString()} loss`}
                </div>
                {incidents && incidents.byCategory.length > 0 && (
                  <div className="mt-1 text-xs text-muted-foreground">
                    {incidents.byCategory.slice(0, 3).map((c) => `${c.key.replace(/([a-z])([A-Z])/g, '$1 $2')} ${c.count}`).join(' · ')}
                  </div>
                )}
              </Link>
            </CardContent>
          </Card>
        )}
        {seesAlerts && (
          <Card>
            <CardContent className="pt-5">
              <Link to="/alerts" className="block">
                <div className="text-3xl font-semibold tabular-nums">
                  {alerts === undefined ? '—' : alerts.unread}
                </div>
                <div className="text-sm text-muted-foreground">
                  unread alerts · {alerts?.activeBulletins ?? 0} active bulletins
                  {alerts && Number(alerts.unacknowledgedBulletins) > 0 && ` · ${alerts.unacknowledgedBulletins} to acknowledge`}
                </div>
              </Link>
            </CardContent>
          </Card>
        )}
      </div>
      {hierarchy && (sites !== undefined || !seesSites) && (
        <SetupList
          levels={hierarchy.levels}
          nodeCount={hierarchy.nodes.length}
          siteCount={sites?.total ?? 0}
          memberCount={members?.total}
          pendingInvites={pending}
          manageHierarchy={manageHierarchy}
          manageSites={manageSites}
          manageMembers={seesMembers}
        />
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        {seesSites && (
          <Stat
            to="/sites"
            icon={MapPin}
            label="Sites"
            value={sites === undefined ? '—' : sites.total.toLocaleString()}
            hint={sites === undefined ? undefined : `${(sites.openCount ?? 0).toLocaleString()} open right now`}
          />
        )}
        {seesMembers && (
          <Stat
            to="/members"
            icon={Users}
            label="Pending invitations"
            value={invitations === undefined ? '—' : pending}
            hint={pending === 0 ? 'Everyone invited has joined' : 'Waiting on a reply'}
          />
        )}
      </div>

      {seesAudit && (
        <Panel
          title="Recent activity"
          actions={
            <Link to="/audit" className="text-sm text-muted-foreground hover:underline">
              All activity →
            </Link>
          }
        >
            {events === undefined ? (
              <Loading text="Loading activity…" rows={3} />
            ) : events.length === 0 ? (
              <EmptyState
                icon={Activity}
                title="Nothing recorded yet"
                description="Changes people make show up here as they happen."
                className="py-6"
              />
            ) : (
              <ul className="divide-y text-sm">
                {events.map((e) => (
                  <li key={e.id} className="flex items-center gap-3 py-2 first:pt-0 last:pb-0">
                    <IconTile variant="soft" size="sm">
                      <Activity />
                    </IconTile>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate">{eventLabel(e.eventName ?? 'unknown')}</span>
                      {e.actorLabel && (
                        <span className="block truncate text-xs text-muted-foreground">{e.actorLabel}</span>
                      )}
                    </span>
                    <span className="shrink-0 text-xs text-muted-foreground">
                      {fmtDateTime(e.occurredAt)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
        </Panel>
      )}

      <Panel title="Plan">
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
                          ? `${planNumber(entry.usage)} of ${planNumber(entry.value)}`
                          : planValue(entry?.value)}
                      </span>
                    </div>
                    {showBar && (
                      <Progress
                        value={Math.max(ratio * 100, 2)}
                        aria-label={`${entitlementLabel(code)} usage`}
                        className={ratio >= 1 ? '**:data-[slot=progress-indicator]:bg-destructive' : undefined}
                      />
                    )}
                  </div>
                );
              })}
          </div>
      </Panel>
    </div>
  );
}
