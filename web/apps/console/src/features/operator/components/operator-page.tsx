import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, Input } from '@locintel/ui';
import { useState } from 'react';
import { fmtDateTime } from '../../../lib/format';
import { useApiMutation } from '../../../lib/mutation';
import { StatusBadge } from '../../../shell';
import { operatorApi } from '../api';
import { useDeadLetters, useOperatorHealth, useOperatorOrgs,
  useOperatorOverview, useOperatorUsers, useSuppressions } from '../hooks';
import { OrganizationControls } from './organization-controls';

/** Entitlement custody + lifecycle: operator-set, tenant-read. */
export function OperatorPage() {
  const orgsQuery = useOperatorOrgs();
  const orgs = orgsQuery.data;
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = orgs?.find((org) => org.id === selectedId);

  if (orgsQuery.isPending)
    return <p className="text-sm text-muted-foreground">Loading operator workspace…</p>;
  if (orgsQuery.isError)
    return <p className="text-sm text-destructive">Could not load operator workspace.</p>;

  return (
    <div className="max-w-4xl space-y-6">
      <h1 className="text-2xl font-semibold">Operator</h1>
      <PlatformOverview />
      <CustomerSearch onPickOrg={setSelectedId} />
      <div className="grid grid-cols-1 gap-6 md:grid-cols-[280px_1fr]">
        <Card>
          <CardHeader><CardTitle>Organizations</CardTitle></CardHeader>
          <CardContent className="space-y-1">
            {orgs?.map((org) => (
              <button
                key={org.id}
                type="button"
                onClick={() => setSelectedId(org.id)}
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
          <OrganizationControls
            key={selected.id}
            org={selected}
            onOffboard={() => setSelectedId((current) => current === selected.id ? null : current)}
          />
        )}
      </div>
      <DeadLetters />
      <Dependencies />
      <Suppressions />
    </div>
  );
}

function Dependencies() {
  const { data } = useOperatorHealth();
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

function Suppressions() {
  const [q, setQ] = useState('');
  const { data: rows } = useSuppressions(q);
  const unsuppress = useApiMutation({
    mutationFn: operatorApi.unsuppress,
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

function CustomerSearch({ onPickOrg }: { onPickOrg: (orgId: string) => void }) {
  const [q, setQ] = useState('');
  const { data: hits } = useOperatorUsers(q);
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

function PlatformOverview() {
  const { data } = useOperatorOverview();
  if (!data) return null;
  const active = Number(data.orgsByStatus.find((s) => s.status === 'Active')?.count ?? 0);
  const suspended = Number(data.orgsByStatus.find((s) => s.status === 'Suspended')?.count ?? 0);
  const stats: [string, number, boolean][] = [
    ['Active orgs', active, false],
    ['Suspended', suspended, suspended > 0],
    ['Closures pending', Number(data.closuresPending), Number(data.closuresPending) > 0],
    ['People', Number(data.users), false],
    ['Dead letters', Number(data.deadLetters), Number(data.deadLetters) > 0],
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

function DeadLetters() {
  const { data } = useDeadLetters();
  const replay = useApiMutation({
    mutationFn: operatorApi.replayDeadLetter,
    invalidate: [['dead-letters']],
    success: 'Requeued for delivery',
  });
  const discard = useApiMutation({
    mutationFn: operatorApi.discardDeadLetter,
    invalidate: [['dead-letters']],
    success: 'Discarded',
  });
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          Dead letters{data && Number(data.total) > 0 ? ` (${data.total})` : ''}
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
