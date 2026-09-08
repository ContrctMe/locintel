import { api } from '@locintel/api';
import { Button, Tabs, TabsList, TabsTrigger } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { ScrollText } from 'lucide-react';
import { useState } from 'react';
import { EmptyState, Loading, PageHeader, Panel } from '../components/page';
import { AuditTimeline, type AuditKind } from '../features/audit/audit-timeline';
import { useApiMutation } from '../lib/mutation';

const KINDS: readonly AuditKind[] = ['events', 'changes', 'authz', 'access'];
const KIND_LABELS: Record<AuditKind, string> = {
  events: 'Events',
  changes: 'Changes',
  authz: 'Access decisions',
  access: 'Request log',
};

/** The audit trail as a day-grouped timeline; one kind at a time, more on demand. */
export function AuditPage() {
  const [kind, setKind] = useState<AuditKind>('events');
  const [limit, setLimit] = useState(50);
  const { data: rows } = useQuery({
    queryKey: ['audit', kind, limit],
    queryFn: ({ signal }) => api.get('/api/audit/{kind}', { path: { kind }, query: { limit }, signal }),
  });
  const exportTrail = useApiMutation({
    mutationFn: () => api.post('/api/audit/export'),
    success: 'Export queued - check Files shortly',
  });

  return (
    <div className="space-y-6">
      <PageHeader
        title="Audit"
        description="Events, changes, access decisions and the request log."
        actions={
          <Button variant="outline" size="sm" disabled={exportTrail.isPending} onClick={() => exportTrail.mutate()}>
            Export trail
          </Button>
        }
      />
      <Tabs
        value={kind}
        onValueChange={(next) => {
          if ((KINDS as readonly string[]).includes(String(next))) {
            setKind(next as AuditKind);
            setLimit(50);
          }
        }}
      >
        <TabsList variant="line" aria-label="Audit kind">
          {KINDS.map((k) => (
            <TabsTrigger key={k} value={k}>
              {KIND_LABELS[k]}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>
      {rows === undefined ? (
        <Loading text="Loading the trail…" rows={4} />
      ) : rows.length === 0 ? (
        <Panel>
          <EmptyState
            icon={ScrollText}
            title="Nothing recorded yet"
            description="Activity in this organization lands here as it happens."
          />
        </Panel>
      ) : (
        <Panel
          footer={
            rows.length >= limit && limit < 500 ? (
              <Button variant="outline" size="sm" onClick={() => setLimit(limit + 100)}>
                Load more
              </Button>
            ) : (
              <span className="text-sm text-muted-foreground">{rows.length} in view</span>
            )
          }
        >
          <AuditTimeline kind={kind} rows={rows} />
        </Panel>
      )}
    </div>
  );
}
