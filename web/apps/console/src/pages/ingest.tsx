import { api, type components } from '@locintel/api';
import { Alert, AlertDescription, AlertTitle, Button, Card, CardContent, CardHeader,
  CardTitle, ConfirmButton, FormDialog, Input, Label,
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@locintel/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import { uploadFile } from '../lib/uploads';

type Connector = components['schemas']['ConnectorResponse'];

export function IngestPage() {
  const queryClient = useQueryClient();
  const [batchId, setBatchId] = useState<string | null>(null);
  const [phase, setPhase] = useState<string>('');
  const csvInput = useRef<HTMLInputElement>(null);

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

  return (
    <div className="max-w-4xl space-y-6">
      <h1 className="text-2xl font-semibold">Site ingest</h1>
      <Card>
        <CardHeader>
          <CardTitle>Upload CSV</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <p className="text-sm text-muted-foreground">
            Columns: external_id, name, time_zone, node, status (open|closed). Nothing is applied
            until you review the diff and commit.
          </p>
          <input
            ref={csvInput}
            type="file"
            accept=".csv,text/csv"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) stage.mutate(file);
              e.target.value = '';
            }}
          />
          <Button disabled={stage.isPending} onClick={() => csvInput.current?.click()}>
            Choose CSV…
          </Button>
          {phase && <p className="text-sm text-muted-foreground">{phase}</p>}
          {stage.isError && (
            <Alert variant="destructive">
              <AlertTitle>Staging failed</AlertTitle>
              <AlertDescription>{String(stage.error)}</AlertDescription>
            </Alert>
          )}
        </CardContent>
      </Card>

      {preview && (
        <Card>
          <CardHeader>
            <CardTitle>
              Diff preview — {preview.counts.create} new, {preview.counts.update} updated,{' '}
              {preview.counts.close} closing, {preview.counts.unchanged} unchanged,{' '}
              {preview.counts.invalid} invalid
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>External id</TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead>Node</TableHead>
                  <TableHead>Action</TableHead>
                  <TableHead>Detail</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {preview.rows.map((r) => (
                  <TableRow key={r.externalId + r.name}>
                    <TableCell className="font-mono text-xs">{r.externalId}</TableCell>
                    <TableCell>{r.name}</TableCell>
                    <TableCell className="text-muted-foreground">{r.nodePath}</TableCell>
                    <TableCell>{r.action}</TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {r.errors.join('; ') || r.changes.join('; ')}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            {preview.status === 'Staged' ? (
              <Button disabled={commit.isPending} onClick={() => commit.mutate()}>
                Commit {Number(preview.counts.create) + Number(preview.counts.update) + Number(preview.counts.close)} changes
              </Button>
            ) : (
              <p className="text-sm text-muted-foreground">Batch is {preview.status}.</p>
            )}
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader><CardTitle>Batches</CardTitle></CardHeader>
        <CardContent>
          {batches && batches.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Source</TableHead>
                  <TableHead>Staged</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Counts</TableHead>
                  <TableHead><span className="sr-only">Actions</span></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {batches.map((b) => (
                  <TableRow key={b.id}>
                    <TableCell>{b.source}</TableCell>
                    <TableCell className="text-muted-foreground">{fmtDateTime(b.createdAt)}</TableCell>
                    <TableCell className="text-muted-foreground">{b.status}</TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      +{b.counts.create} ~{b.counts.update} −{b.counts.close} ·{' '}
                      {b.counts.invalid} invalid
                    </TableCell>
                    <TableCell className="space-x-1 text-right">
                      {b.status === 'Staged' && (
                        <>
                          <Button variant="ghost" size="sm" onClick={() => setBatchId(b.id)}>
                            Review
                          </Button>
                          <ConfirmButton
                            size="sm"
                            disabled={discard.isPending}
                            onConfirm={() => discard.mutate(b.id)}
                          >
                            Discard
                          </ConfirmButton>
                        </>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <p className="text-sm text-muted-foreground">No batches yet.</p>
          )}
        </CardContent>
      </Card>

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

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between">
          Connectors
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
              <div className="space-y-1">
                <Label htmlFor="conn-name">Name</Label>
                <Input id="conn-name" value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="conn-url">URL</Label>
                <Input id="conn-url" value={form.url}
                  onChange={(e) => setForm({ ...form, url: e.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="conn-key">
                  API key{editing ? ' (leave blank to keep current)' : ''}
                </Label>
                <Input id="conn-key" type="password" value={form.apiKey}
                  onChange={(e) => setForm({ ...form, apiKey: e.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="conn-interval">Sync every N hours (blank = manual)</Label>
                <Input id="conn-interval" type="number" min="1" value={form.interval}
                  onChange={(e) => setForm({ ...form, interval: e.target.value })} />
              </div>
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
        </CardTitle>
      </CardHeader>
      <CardContent>
        {connectors && connectors.length > 0 ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Schedule</TableHead>
                <TableHead>Last sync</TableHead>
                <TableHead className="w-52"><span className="sr-only">Actions</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {connectors.map((c) => (
                <TableRow key={c.id}>
                  <TableCell>
                    <div>{c.name}</div>
                    <div className="max-w-64 truncate text-xs text-muted-foreground">{c.url}</div>
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {c.syncIntervalHours ? `every ${c.syncIntervalHours}h` : 'manual'}
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {c.lastSyncedAt ? fmtDateTime(c.lastSyncedAt) : 'never'}
                  </TableCell>
                  <TableCell className="space-x-1 text-right">
                    <Button variant="ghost" size="sm" disabled={sync.isPending}
                      onClick={() => sync.mutate(c.id)}>
                      Sync now
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => openEdit(c)}>
                      Edit
                    </Button>
                    <ConfirmButton size="sm" disabled={remove.isPending}
                      onConfirm={() => remove.mutate(c.id)}>
                      Delete
                    </ConfirmButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p className="text-sm text-muted-foreground">
            No connectors yet. Add one to pull sites from an external source.
          </p>
        )}
      </CardContent>
    </Card>
  );
}
