import { api, ENTITLEMENTS, type EntitlementCode } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, Input } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { entitlementLabel, fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import { StatusBadge } from '../shell';

type OperatedOrg = {
  id: string; name: string; slug: string; status: string;
  isPlatform: boolean; createdAt: string;
};
type Effective = Record<string, { value: string; shape: string; policy: string }>;

/** Entitlement custody + lifecycle: operator-set, tenant-read. */
export function OperatorPage() {
  const { data: orgs } = useQuery({
    queryKey: ['operator-orgs'],
    queryFn: () => api.get<OperatedOrg[]>('/api/operator/orgs'),
  });
  const [selected, setSelected] = useState<OperatedOrg | null>(null);

  const transition = useApiMutation({
    mutationFn: (input: { orgId: string; action: 'suspend' | 'reactivate' }) =>
      api.post(`/api/operator/orgs/${input.orgId}/${input.action}`),
    invalidate: [['operator-orgs']],
    success: 'Status updated',
  });
  const exportOrg = useApiMutation({
    mutationFn: (orgId: string) => api.post(`/api/operator/orgs/${orgId}/export`),
    success: "Export queued - it lands in the org's Files",
  });
  const impersonate = useApiMutation({
    mutationFn: (orgId: string) =>
      api.post<{ expiresAt: string }>(`/api/operator/orgs/${orgId}/impersonate`),
    onSuccess: () => {
      location.href = '/'; // full reload: the whole session re-resolves as the target org
    },
  });
  const offboard = useApiMutation({
    mutationFn: (orgId: string) => api.post(`/api/operator/orgs/${orgId}/offboard`),
    invalidate: [['operator-orgs']],
    success: 'Offboarding started',
    onSuccess: () => setSelected(null),
  });

  return (
    <div className="max-w-4xl space-y-6">
      <h1 className="text-2xl font-semibold">Operator</h1>
      <PlatformOverview />
      <CustomerSearch onPickOrg={(id) => setSelected(orgs?.find((o) => o.id === id) ?? null)} />
      <div className="grid grid-cols-1 gap-6 md:grid-cols-[280px_1fr]">
        <Card>
          <CardHeader><CardTitle>Organizations</CardTitle></CardHeader>
          <CardContent className="space-y-1">
            {orgs?.map((org) => (
              <button
                key={org.id}
                type="button"
                onClick={() => setSelected(org)}
                className={`flex w-full items-center justify-between rounded-md px-2 py-1.5 text-left text-sm hover:bg-accent ${
                  selected?.id === org.id ? 'bg-accent' : ''
                }`}
              >
                <span>
                  {org.name}
                  {org.isPlatform && <span className="ml-1 text-xs text-muted-foreground">(platform)</span>}
                </span>
                <StatusBadge status={org.status} />
              </button>
            ))}
          </CardContent>
        </Card>
        {selected && !selected.isPlatform && (
          <div className="space-y-4">
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center justify-between">
                  {selected.name}
                  <span className="flex items-center gap-2">
                  {selected.status === 'Active' && (
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={impersonate.isPending}
                      onClick={() => impersonate.mutate(selected.id)}
                    >
                      Impersonate
                    </Button>
                  )}
                  {selected.status === 'Active' ? (
                    <Button
                      variant="destructive"
                      size="sm"
                      disabled={transition.isPending}
                      onClick={() => transition.mutate({ orgId: selected.id, action: 'suspend' })}
                    >
                      Suspend
                    </Button>
                  ) : (
                    <Button
                      size="sm"
                      disabled={transition.isPending}
                      onClick={() => transition.mutate({ orgId: selected.id, action: 'reactivate' })}
                    >
                      Reactivate
                    </Button>
                  )}
                  </span>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <OrgEntitlements orgId={selected.id} />
              </CardContent>
            </Card>
            <Card>
              <CardHeader><CardTitle>Lifecycle</CardTitle></CardHeader>
              <CardContent className="space-y-3">
                <div className="flex items-center gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={exportOrg.isPending}
                    onClick={() => exportOrg.mutate(selected.id)}
                  >
                    Export data
                  </Button>
                  <span className="text-sm text-muted-foreground">
                    {exportOrg.isSuccess
                      ? "Queued - the archive lands in the org's Files."
                      : 'Full data archive, delivered to the org’s file library.'}
                  </span>
                </div>
                <div className="flex items-center gap-2">
                  <ConfirmButton
                    variant="destructive"
                    size="sm"
                    confirmLabel="Purge org data?"
                    disabled={offboard.isPending || selected.status !== 'Suspended'}
                    onConfirm={() => offboard.mutate(selected.id)}
                  >
                    Offboard
                  </ConfirmButton>
                  <span className="text-sm text-muted-foreground">
                    {selected.status === 'Suspended'
                      ? 'Purges all org data. The audit trail and org record remain.'
                      : 'Suspend the org first - offboarding is a deliberate two-step.'}
                  </span>
                </div>
              </CardContent>
            </Card>
          </div>
        )}
      </div>
      <DeadLetters />
      <Dependencies />
      <Suppressions />
    </div>
  );
}

type HealthCheck = { name: string; ok: boolean; latencyMs: number; error: string | null };

function Dependencies() {
  const { data } = useQuery({
    queryKey: ['operator-health'],
    queryFn: () => api.get<{ checks: HealthCheck[] }>('/api/operator/health'),
    refetchInterval: 60_000,
  });
  if (!data) return null;
  return (
    <Card>
      <CardHeader><CardTitle>Dependencies</CardTitle></CardHeader>
      <CardContent className="flex flex-wrap gap-2">
        {data.checks.map((c) => (
          <span key={c.name}
            title={c.error ?? `${c.latencyMs}ms`}
            className={`rounded-md border px-2 py-1 text-sm ${c.ok ? '' : 'border-destructive text-destructive'}`}>
            {c.ok ? '●' : '○'} {c.name}
            <span className="ml-1 text-xs text-muted-foreground">
              {c.ok ? `${c.latencyMs}ms` : c.error}
            </span>
          </span>
        ))}
      </CardContent>
    </Card>
  );
}

type Suppression = { id: string; email: string; reason: string; createdAt: string };

function Suppressions() {
  const [q, setQ] = useState('');
  const { data: rows } = useQuery({
    queryKey: ['suppressions', q],
    queryFn: () =>
      api.get<Suppression[]>(`/api/operator/suppressions${q.trim() ? `?q=${encodeURIComponent(q)}` : ''}`),
  });
  const unsuppress = useApiMutation({
    mutationFn: (id: string) => api.del(`/api/operator/suppressions/${id}`),
    invalidate: [['suppressions']],
    success: 'Unsuppressed - sending to this address resumes',
  });
  return (
    <Card>
      <CardHeader><CardTitle>Email suppressions</CardTitle></CardHeader>
      <CardContent className="space-y-2">
        <p className="text-sm text-muted-foreground">
          Addresses that bounced. Verify the address is real before unsuppressing -
          repeated bounces hurt the platform&apos;s sender reputation.
        </p>
        <Input placeholder="Search addresses…" value={q} onChange={(e) => setQ(e.target.value)} />
        {rows?.length === 0 && (
          <p className="text-sm text-muted-foreground">Nothing suppressed.</p>
        )}
        {rows?.map((s) => (
          <div key={s.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
            <span>
              <span className="font-medium">{s.email}</span>
              <span className="ml-2 text-muted-foreground">
                {s.reason} · {fmtDateTime(s.createdAt)}
              </span>
            </span>
            <ConfirmButton size="sm" variant="outline" confirmLabel="Verified real address?"
              disabled={unsuppress.isPending} onConfirm={() => unsuppress.mutate(s.id)}>
              Unsuppress
            </ConfirmButton>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

type FoundUser = {
  id: string;
  email: string;
  name: string | null;
  orgs: { id: string; name: string; status: string }[];
};

function CustomerSearch({ onPickOrg }: { onPickOrg: (orgId: string) => void }) {
  const [q, setQ] = useState('');
  const { data: hits } = useQuery({
    queryKey: ['operator-users', q],
    queryFn: () => api.get<FoundUser[]>(`/api/operator/users?q=${encodeURIComponent(q)}`),
    enabled: q.trim().length >= 2,
  });
  return (
    <Card>
      <CardHeader><CardTitle>Find a customer</CardTitle></CardHeader>
      <CardContent className="space-y-2">
        <Input placeholder="Email or name from the ticket…" value={q}
          onChange={(e) => setQ(e.target.value)} />
        {q.trim().length >= 2 && hits?.length === 0 && (
          <p className="text-sm text-muted-foreground">No people match.</p>
        )}
        {hits?.map((u) => (
          <div key={u.id} className="flex flex-wrap items-center justify-between gap-2 rounded-md border p-2 text-sm">
            <span>
              <span className="font-medium">{u.email}</span>
              {u.name && <span className="ml-2 text-muted-foreground">{u.name}</span>}
            </span>
            <span className="flex flex-wrap gap-1">
              {u.orgs.length === 0 && <span className="text-muted-foreground">no orgs</span>}
              {u.orgs.map((o) => (
                <Button key={o.id} variant="outline" size="sm" onClick={() => onPickOrg(o.id)}>
                  {o.name}
                  {o.status !== 'Active' && (
                    <span className="ml-1 text-muted-foreground">({o.status})</span>
                  )}
                </Button>
              ))}
            </span>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

type Overview = {
  orgsByStatus: { status: string; count: number }[];
  closuresPending: number;
  users: number;
  deadLetters: number;
};

function PlatformOverview() {
  const { data } = useQuery({
    queryKey: ['operator-overview'],
    queryFn: () => api.get<Overview>('/api/operator/overview'),
    refetchInterval: 30_000,
  });
  if (!data) return null;
  const active = data.orgsByStatus.find((s) => s.status === 'Active')?.count ?? 0;
  const suspended = data.orgsByStatus.find((s) => s.status === 'Suspended')?.count ?? 0;
  const stats: [string, number, boolean][] = [
    ['Active orgs', active, false],
    ['Suspended', suspended, suspended > 0],
    ['Closures pending', data.closuresPending, data.closuresPending > 0],
    ['People', data.users, false],
    ['Dead letters', data.deadLetters, data.deadLetters > 0],
  ];
  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
      {stats.map(([label, value, attention]) => (
        <Card key={label}>
          <CardContent className="pt-4">
            <div className={`text-2xl font-semibold ${attention ? 'text-warning-foreground' : ''}`}>
              {value}
            </div>
            <div className="text-xs text-muted-foreground">{label}</div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

type DeadLetter = {
  id: string;
  messageType: string;
  exceptionType: string;
  exceptionMessage: string;
  sentAt: string;
  tenantId?: string | null;
  replayable: boolean;
};

function DeadLetters() {
  const { data } = useQuery({
    queryKey: ['dead-letters'],
    queryFn: () => api.get<{ total: number; items: DeadLetter[] }>('/api/operator/dead-letters'),
    refetchInterval: 30_000,
  });
  const replay = useApiMutation({
    mutationFn: (id: string) => api.post(`/api/operator/dead-letters/${id}/replay`),
    invalidate: [['dead-letters']],
    success: 'Requeued for delivery',
  });
  const discard = useApiMutation({
    mutationFn: (id: string) => api.del(`/api/operator/dead-letters/${id}`),
    invalidate: [['dead-letters']],
    success: 'Discarded',
  });
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          Dead letters{data && data.total > 0 ? ` (${data.total})` : ''}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {data?.total === 0 && (
          <p className="text-sm text-muted-foreground">
            No failed messages. Background work that fails after retries lands here for
            replay or discard.
          </p>
        )}
        {data?.items.map((d) => (
          <div key={d.id} className="flex items-start justify-between gap-3 rounded-md border p-2 text-sm">
            <div className="min-w-0">
              <div className="font-medium">
                {d.messageType}
                {d.replayable && <span className="ml-2 text-xs text-muted-foreground">requeued…</span>}
              </div>
              <div className="truncate text-xs text-muted-foreground" title={d.exceptionMessage}>
                {d.exceptionType}: {d.exceptionMessage}
              </div>
              {d.tenantId && <div className="text-xs text-muted-foreground">org {d.tenantId}</div>}
            </div>
            <div className="flex shrink-0 gap-1">
              <Button size="sm" variant="outline" disabled={replay.isPending || d.replayable}
                onClick={() => replay.mutate(d.id)}>
                Replay
              </Button>
              <ConfirmButton size="sm" variant="destructive" confirmLabel="Discard forever?"
                disabled={discard.isPending} onConfirm={() => discard.mutate(d.id)}>
                Discard
              </ConfirmButton>
            </div>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

function OrgEntitlements({ orgId }: { orgId: string }) {
  const { data: effective } = useQuery({
    queryKey: ['operator-entitlements', orgId],
    queryFn: () => api.get<Effective>(`/api/operator/orgs/${orgId}/entitlements`),
  });
  const [drafts, setDrafts] = useState<Record<string, string>>({});

  const set = useApiMutation({
    mutationFn: (input: { code: string; value: string }) =>
      api.put(`/api/operator/orgs/${orgId}/entitlements/${input.code}`, { value: input.value }),
    invalidate: [['operator-entitlements', orgId]],
    success: 'Entitlement updated',
    errorFallback: 'Update failed',
  });

  return (
    <div className="space-y-2">
      {effective &&
        (Object.keys(ENTITLEMENTS) as EntitlementCode[]).map((code) => {
          const draft = drafts[code] ?? effective[code]?.value ?? '';
          const dirty = draft !== effective[code]?.value;
          return (
            <div key={code} className="flex items-center gap-2 text-sm">
              <span className="w-56 text-muted-foreground" title={code}>
                {entitlementLabel(code)}
              </span>
              <Input
                className="h-8 w-32"
                value={draft}
                onChange={(e) => setDrafts({ ...drafts, [code]: e.target.value })}
              />
              {dirty && (
                <Button size="sm" disabled={set.isPending}
                  onClick={() => set.mutate({ code, value: draft })}>
                  Save
                </Button>
              )}
            </div>
          );
        })}
    </div>
  );
}
