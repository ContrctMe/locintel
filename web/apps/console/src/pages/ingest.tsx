import { api, type components } from '@locintel/api';
import { Alert, AlertDescription, AlertTitle, Button, ConfirmButton, Field, FieldLabel, FormDialog, Input, type ColumnDef, type DataGridFeatures, CodeBlock, Stepper, StepperIndicator, StepperItem, StepperNav, StepperSeparator, StepperTitle, StepperTrigger } from '@locintel/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { Grid, PageHeader, Panel } from '../components/page';
import { FileDropzone } from '../components/file-dropzone';
import { useApiMutation } from '../lib/mutation';
import { StatusBadge } from '../shell';
import { uploadFile } from '../lib/uploads';
import { Link } from '@tanstack/react-router';
import { CheckCircle2 } from 'lucide-react';

/** The invalid rows back as a CSV the uploader can fix and re-run (one row per problem row, its reasons in the last column). */
function downloadInvalidRows(rows: { externalId: string; name: string; nodePath: string; errors: string[] }[]) {
  const quote = (v: string) => `"${v.replaceAll('"', '""')}"`;
  const csv = ['external_id,name,node,problems', ...rows.map((r) => [r.externalId, r.name, r.nodePath, r.errors.join('; ')].map(quote).join(','))].join('\n');
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
  const a = document.createElement('a');
  a.href = url;
  a.download = 'invalid-rows.csv';
  a.click();
  URL.revokeObjectURL(url);
}

const STEPS = ['Upload', 'Review the diff', 'Commit'] as const;

type Connector = components['schemas']['ConnectorResponse'];

export function IngestPage() {
  const queryClient = useQueryClient();
  const [batchId, setBatchId] = useState<string | null>(null);
  const [phase, setPhase] = useState<string>('');

  const stage = useMutation({
    mutationFn: async (file: File) => {
      const fileId = await uploadFile(file, 'text/csv', setPhase);
      setPhase('Computing diff…');
      const staged = await api.post('/api/ingest/uploads', { fileId });
      return staged.batchId;
    },
    onSuccess: (id) => {
      setBatchId(id);
      setPhase('');
    },
    onError: () => setPhase(''),
  });

  const { data: preview } = useQuery({
    queryKey: ['ingest-batch', batchId],
    // enabled only with a batch; the typed path refuses a null id
    queryFn: ({ signal }) => api.get('/api/ingest/batches/{id}', { path: { id: batchId ?? '' }, signal }),
    enabled: batchId !== null,
  });

  const commit = useMutation({
    mutationFn: () => {
      if (batchId === null) throw new Error('no staged batch to commit');
      return api.post('/api/ingest/batches/{id}/commit', undefined, { path: { id: batchId } });
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['ingest-batch', batchId] });
      void queryClient.invalidateQueries({ queryKey: ['ingest-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['sites'] });
    },
  });
  const { data: batches } = useQuery({
    queryKey: ['ingest-batches'],
    queryFn: ({ signal }) => api.get('/api/ingest/batches', { signal }),
  });
  const discard = useApiMutation({
    mutationFn: (id: string) => api.post('/api/ingest/batches/{id}/discard', undefined, { path: { id } }),
    invalidate: [['ingest-batches']],
    success: 'Batch discarded',
    onSuccess: (_, id) => {
      if (id === batchId) setBatchId(null);
    },
  });

  type PreviewRow = NonNullable<typeof preview>['rows'][number];
  const previewColumns = useMemo<ColumnDef<DataGridFeatures, PreviewRow>[]>(
    () => [
      { id: 'externalId', accessorKey: 'externalId', header: 'External id', cell: ({ row }) => <span className="font-mono text-xs">{row.original.externalId}</span> },
      { id: 'name', accessorKey: 'name', header: 'Name' },
      { id: 'node', accessorKey: 'nodePath', header: 'Node', cell: ({ row }) => <span className="text-muted-foreground">{row.original.nodePath}</span> },
      { id: 'action', accessorKey: 'action', header: 'Action' },
      {
        id: 'detail',
        header: 'Detail',
        cell: ({ row }) => <span className="text-xs text-muted-foreground">{row.original.errors.join('; ') || row.original.changes.join('; ')}</span>,
      },
    ],
    [],
  );
  type BatchRow = NonNullable<typeof batches>[number];
  const batchColumns = useMemo<ColumnDef<DataGridFeatures, BatchRow>[]>(
    () => [
      { id: 'source', accessorKey: 'source', header: 'Source' },
      { id: 'staged', accessorKey: 'createdAt', header: 'Staged', cell: ({ row }) => <span className="text-muted-foreground">{fmtDateTime(row.original.createdAt)}</span> },
      { id: 'status', accessorKey: 'status', header: 'Status', cell: ({ row }) => <StatusBadge status={row.original.status} /> },
      {
        id: 'counts',
        header: 'Counts',
        cell: ({ row }) => (
          <span className="text-xs text-muted-foreground">
            +{row.original.counts.create} ~{row.original.counts.update} −{row.original.counts.close} · {row.original.counts.invalid} invalid
          </span>
        ),
      },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) =>
          row.original.status === 'Staged' ? (
            <div className="space-x-1 text-right">
              <Button variant="ghost" size="sm" onClick={() => setBatchId(row.original.id)}>
                Review
              </Button>
              <ConfirmButton size="sm" confirmLabel="Discard this batch?" disabled={discard.isPending} onConfirm={() => discard.mutate(row.original.id)}>
                Discard
              </ConfirmButton>
            </div>
          ) : null,
        meta: { headerClassName: 'w-40' },
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  const step = preview === undefined ? 1 : preview.status === 'Committed' ? 3 : 2;
  const changes = preview
    ? preview.counts.create + preview.counts.update + preview.counts.close
    : 0;
  const invalid = preview?.counts.invalid ?? 0;
  const invalidRows = preview?.rows.filter((r) => r.errors.length > 0) ?? [];
  return (
    <div className="max-w-4xl space-y-6">
      <PageHeader title="Site ingest" description="Bulk-load sites from a CSV or a connector; nothing applies until you review the diff and commit." />
      {/* where a batch is: the ReUI Stepper, read-only, follows the batch's status */}
      <Stepper value={step} orientation="horizontal" className="max-w-xl">
        <StepperNav>
          {STEPS.map((label, i) => (
            <StepperItem key={label} step={i + 1} completed={step > i + 1}>
              <StepperTrigger className="pointer-events-none">
                <StepperIndicator>{i + 1}</StepperIndicator>
                <StepperTitle>{label}</StepperTitle>
              </StepperTrigger>
              {i < STEPS.length - 1 && <StepperSeparator />}
            </StepperItem>
          ))}
        </StepperNav>
      </Stepper>
      <Panel title="Upload CSV" bodyClassName="space-y-3">
          <p className="text-sm text-muted-foreground">
            One row per site. The node column is the node&apos;s name path below the root (or its id); status is open or closed.
            Nothing is applied until you review the diff and commit.
          </p>
          <CodeBlock
            code={'external_id,name,time_zone,node,status\nstore-001,Northgate,America/Los_Angeles,Pacific Northwest/Seattle,open'}
            language="csv"
            highlight={false}
          />
          <FileDropzone
            accept=".csv,text/csv"
            onFile={(file) => stage.mutate(file)}
            busy={stage.isPending}
            phase="Staging…"
            label="Drop a CSV here, or choose one"
            buttonLabel="Choose CSV…"
          />
          {phase && <p className="text-sm text-muted-foreground">{phase}</p>}
          {stage.isError && (
            <Alert variant="destructive">
              <AlertTitle>Staging failed</AlertTitle>
              <AlertDescription>{String(stage.error)}</AlertDescription>
            </Alert>
          )}
        </Panel>

      {preview && (
        <Grid
          title={
            <>
              Diff preview — {preview.counts.create} new, {preview.counts.update} updated,{' '}
              {preview.counts.close} closing, {preview.counts.unchanged} unchanged,{' '}
              {preview.counts.invalid} invalid
            </>
          }
          columns={previewColumns}
          rows={preview.rows}
          getRowId={(r) => r.externalId + r.name}
          emptyMessage="Nothing in this batch."
          footer={
            preview.status === 'Staged' ? (
              <>
                <Button disabled={commit.isPending || changes === 0} onClick={() => commit.mutate()}>
                  Commit {changes} {changes === 1 ? 'row' : 'rows'}
                </Button>
                {invalid > 0 && (
                  <span className="text-sm text-muted-foreground">
                    {invalid} invalid {invalid === 1 ? 'row is' : 'rows are'} skipped.{' '}
                    <Button variant="link" size="sm" className="h-auto px-0" onClick={() => downloadInvalidRows(invalidRows)}>
                      Download them
                    </Button>
                  </span>
                )}
              </>
            ) : preview.status === 'Committed' ? (
              <Alert className="w-full">
                <CheckCircle2 aria-hidden />
                <AlertTitle>Committed</AlertTitle>
                <AlertDescription>
                  <p>
                    {preview.counts.create} new, {preview.counts.update} updated, {preview.counts.close} closed
                    {invalid > 0 && `, ${invalid} invalid skipped`}.
                  </p>
                  <div className="flex flex-wrap gap-3">
                    <Link to="/sites" className="underline underline-offset-4">View sites</Link>
                    {invalid > 0 && (
                      <Button variant="link" size="sm" className="h-auto px-0" onClick={() => downloadInvalidRows(invalidRows)}>
                        Download the invalid rows
                      </Button>
                    )}
                  </div>
                </AlertDescription>
              </Alert>
            ) : (
              <p className="text-sm text-muted-foreground">Batch is {preview.status}.</p>
            )
          }
        />
      )}

      <Grid
        title="Batches"
        columns={batchColumns}
        rows={batches ?? []}
        getRowId={(b) => b.id}
        isLoading={batches === undefined}
        emptyMessage="No batches yet."
      />

      <ConnectorsCard />
    </div>
  );
}

function ConnectorsCard() {
  const { data: connectors } = useQuery({
    queryKey: ['connectors'],
    queryFn: ({ signal }) => api.get('/api/connectors', { signal }),
  });
  const empty = { name: '', url: '', apiKey: '', interval: '' };
  const [form, setForm] = useState(empty);
  const [editing, setEditing] = useState<string | null>(null);
  const [open, setOpen] = useState(false);

  const openCreate = () => {
    setEditing(null);
    setForm(empty);
    setOpen(true);
  };
  const openEdit = (c: Connector) => {
    setEditing(c.id);
    setForm({
      name: c.name,
      url: c.url,
      apiKey: '',
      interval: c.syncIntervalHours?.toString() ?? '',
    });
    setOpen(true);
  };

  const save = useApiMutation({
    mutationFn: () => {
      const body = {
        name: form.name.trim(),
        url: form.url.trim(),
        apiKey: form.apiKey || undefined,
        syncIntervalHours: form.interval ? Number(form.interval) : null,
      };
      return editing
        ? api.put('/api/connectors/{id}', body, { path: { id: editing } })
        : api.post('/api/connectors', { ...body, apiKey: form.apiKey });
    },
    invalidate: [['connectors']],
    success: 'Connector saved',
    onSuccess: () => setOpen(false),
  });
  const sync = useApiMutation({
    mutationFn: (id: string) => api.post('/api/connectors/{id}/sync', undefined, { path: { id } }),
    invalidate: [['ingest-batches']],
    success: 'Sync queued - the batch lands under Batches',
  });
  const remove = useApiMutation({
    mutationFn: (id: string) => api.del('/api/connectors/{id}', { path: { id } }),
    invalidate: [['connectors']],
    success: 'Connector deleted',
  });
  const connectorColumns = useMemo<ColumnDef<DataGridFeatures, Connector>[]>(
    () => [
      {
        id: 'name',
        accessorKey: 'name',
        header: 'Name',
        cell: ({ row }) => (
          <div className="min-w-0">
            <div>{row.original.name}</div>
            <div className="max-w-64 truncate text-xs text-muted-foreground">{row.original.url}</div>
          </div>
        ),
      },
      { id: 'schedule', header: 'Schedule', cell: ({ row }) => <span className="text-muted-foreground">{row.original.syncIntervalHours ? `every ${row.original.syncIntervalHours}h` : 'manual'}</span> },
      { id: 'lastSync', header: 'Last sync', cell: ({ row }) => <span className="text-muted-foreground">{row.original.lastSyncedAt ? fmtDateTime(row.original.lastSyncedAt) : 'never'}</span> },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) => (
          <div className="space-x-1 text-right">
            <Button variant="ghost" size="sm" disabled={sync.isPending} onClick={() => sync.mutate(row.original.id)}>
              Sync now
            </Button>
            <Button variant="ghost" size="sm" onClick={() => openEdit(row.original)}>
              Edit
            </Button>
            <ConfirmButton size="sm" confirmLabel="Delete this connector?" disabled={remove.isPending} onConfirm={() => remove.mutate(row.original.id)}>
              Delete
            </ConfirmButton>
          </div>
        ),
        meta: { headerClassName: 'w-52' },
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  return (
    <Grid
      title="Connectors"
      actions={
<FormDialog
            open={open}
            onOpenChange={setOpen}
            trigger={
              <Button variant="outline" size="sm" onClick={openCreate}>
                Add connector
              </Button>
            }
            title={editing ? 'Edit connector' : 'Add connector'}
            description="A pull source your sites sync from. Credentials are envelope-encrypted at rest."
          >
            <div className="space-y-3">
              <Field>
                <FieldLabel htmlFor="conn-name">Name</FieldLabel>
                <Input id="conn-name" value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })} />
              </Field>
              <Field>
                <FieldLabel htmlFor="conn-url">URL</FieldLabel>
                <Input id="conn-url" value={form.url}
                  onChange={(e) => setForm({ ...form, url: e.target.value })} />
              </Field>
              <Field>
                <FieldLabel htmlFor="conn-key">
                  API key{editing ? ' (leave blank to keep current)' : ''}
                </FieldLabel>
                <Input id="conn-key" type="password" value={form.apiKey}
                  onChange={(e) => setForm({ ...form, apiKey: e.target.value })} />
              </Field>
              <Field>
                <FieldLabel htmlFor="conn-interval">Sync every N hours (blank = manual)</FieldLabel>
                <Input id="conn-interval" type="number" min="1" value={form.interval}
                  onChange={(e) => setForm({ ...form, interval: e.target.value })} />
              </Field>
              <Button className="w-full"
                disabled={
                  !form.name.trim() || !form.url.trim() || (!editing && !form.apiKey)
                  || save.isPending
                }
                onClick={() => save.mutate()}>
                {editing ? 'Save changes' : 'Add connector'}
              </Button>
            </div>
          </FormDialog>
      }
      columns={connectorColumns}
      rows={connectors ?? []}
      getRowId={(c) => c.id}
      isLoading={connectors === undefined}
      emptyMessage="No connectors yet. Add one to pull sites from an external source."
    />
  );
}
