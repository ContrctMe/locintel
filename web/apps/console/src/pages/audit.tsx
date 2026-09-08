import { api, type components } from '@locintel/api';
import { Button, Card, CardContent, Table, TableBody, TableCell, TableHead,
  TableHeader, TableRow } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Fragment, useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';

const KINDS = ['events', 'changes', 'authz', 'access'] as const;
type Kind = (typeof KINDS)[number];
const KIND_LABELS: Record<Kind, string> = {
  events: 'Events',
  changes: 'Changes',
  authz: 'Access decisions',
  access: 'Request log',
};
type Row = components['schemas']['AuditRowResponse'];

export function AuditPage() {
  const [kind, setKind] = useState<Kind>('events');
  const [limit, setLimit] = useState(50);
  const [expanded, setExpanded] = useState<string | null>(null);
  const { data: rows } = useQuery({
    queryKey: ['audit', kind, limit],
    queryFn: ({ signal }) => api.get('/api/audit/{kind}', { path: { kind }, query: { limit }, signal }),
  });
  const exportTrail = useApiMutation({
    mutationFn: () => api.post('/api/audit/export'),
    success: 'Export queued - check Files shortly',
  });

  const detail = (row: Row): string => {
    switch (kind) {
      case 'events':
        return `${String(row.eventName)} ${String(row.payload ?? '')}`;
      case 'changes':
        return `${String(row.operation)} ${String(row.schemaName)}.${String(row.tableName)} ${String(row.diff ?? '')}`;
      case 'authz':
        return `${String(row.action)} → ${String(row.outcome)} (${String(row.scopeSummary)})`;
      case 'access':
        return `${String(row.method)} ${String(row.path)} → ${String(row.statusCode)}`;
    }
  };

  return (
    <div className="max-w-5xl space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Audit</h1>
        <Button variant="outline" size="sm" disabled={exportTrail.isPending}
          onClick={() => exportTrail.mutate()}>
          Export trail
        </Button>
      </div>
      <div className="flex gap-2">
        {KINDS.map((k) => (
          <Button key={k} size="sm" variant={k === kind ? 'default' : 'outline'}
            onClick={() => {
              setKind(k);
              setLimit(50);
              setExpanded(null);
            }}>
            {KIND_LABELS[k]}
          </Button>
        ))}
      </div>
      <Card>
        <CardContent className="pt-4">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-40">When</TableHead>
                <TableHead className="w-44">Actor</TableHead>
                <TableHead>Detail</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows === undefined && (
                <TableRow>
                  <TableCell colSpan={3} className="text-center text-muted-foreground">
                    Loading…
                  </TableCell>
                </TableRow>
              )}
              {rows?.map((row) => (
                <Fragment key={row.id}>
                  <TableRow
                    className="cursor-pointer"
                    onClick={() => setExpanded(expanded === row.id ? null : row.id)}
                  >
                    <TableCell className="text-xs text-muted-foreground">
                      {fmtDateTime(row.occurredAt)}
                    </TableCell>
                    <TableCell className="max-w-44 truncate text-xs" title={row.actorLabel ?? row.actorTier}>
                      {row.actorLabel ?? row.actorTier}
                    </TableCell>
                    <TableCell className="max-w-xl truncate font-mono text-xs">
                      {detail(row)}
                    </TableCell>
                  </TableRow>
                  {expanded === row.id && (
                    <TableRow>
                      <TableCell colSpan={3} className="bg-muted/40">
                        <pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all p-1 font-mono text-xs">
                          {JSON.stringify(row, null, 2)}
                        </pre>
                      </TableCell>
                    </TableRow>
                  )}
                </Fragment>
              ))}
              {rows?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={3} className="text-center text-muted-foreground">
                    Nothing recorded yet.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
          {rows && rows.length >= limit && limit < 500 && (
            <div className="pt-3 text-center">
              <Button variant="outline" size="sm" onClick={() => setLimit(limit + 100)}>
                Load more
              </Button>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
