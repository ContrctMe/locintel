import { api } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog,
  Input, Label, Select, Textarea } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import type { Page } from '../lib/paging';
import { can, useMe } from '../session';
import { StatusBadge } from '../shell';
import { EntityStatusBadge, LINK_ROLES, type EntitySummary } from './entities';
import { categoryLabel, SeverityBadge } from './incidents';

type Note = { id: string; authorId: string; author: string | null; body: string; createdAt: string };
type Attachment = {
  id: string; fileId: string; fileName: string | null; contentType: string | null;
  fileStatus: string | null; label: string | null; addedAt: string;
};
type Incident = {
  id: string; siteId: string; category: string; severity: string; status: string;
  source: string; title: string; narrative: string; locationDetail: string | null;
  occurredAt: string; businessDate: string; reportedAt: string; reporter: string | null;
  lossAmount: number | null; recoveredAmount: number | null; currency: string;
  policeReportNumber: string | null; tags: string[]; closedAt: string | null;
  closureReason: string | null; legalHold: boolean; deletedAt: string | null;
  notes: Note[]; attachments: Attachment[];
};
type Site = { id: string; name: string; timeZone: string };
type StoredFile = { id: string; name: string; status: string };

export function IncidentDetailPage() {
  const { incidentId } = useParams({ strict: false }) as { incidentId: string };
  const { data: me } = useMe();
  const manage = can(me, 'incidents:manage');
  const report = can(me, 'incidents:report');
  const key = ['incidents', 'detail', incidentId];
  const { data: incident } = useQuery({
    queryKey: key,
    queryFn: () => api.get<Incident>(`/api/incidents/${incidentId}`),
  });
  const { data: sites } = useQuery({
    queryKey: ['sites', 'picker'],
    queryFn: async () => (await api.get<Page<Site>>('/api/sites?limit=200')).items,
  });
  const site = sites?.find((s) => s.id === incident?.siteId);
  const [reason, setReason] = useState('');
  const [note, setNote] = useState('');

  const act = (path: string, success: string, body?: unknown) =>
    useApiMutation({
      mutationFn: () => api.post(`/api/incidents/${incidentId}${path}`, body),
      invalidate: [['incidents']],
      success,
    });
  const close = useApiMutation({
    mutationFn: () => api.post(`/api/incidents/${incidentId}/close`, { reason: reason.trim() }),
    invalidate: [['incidents']],
    success: 'Incident closed',
    onSuccess: () => setReason(''),
  });
  const reopen = act('/reopen', 'Incident reopened');
  const hold = useApiMutation({
    mutationFn: (value: boolean) =>
      api.post(`/api/incidents/${incidentId}/hold`, { hold: value }),
    invalidate: [['incidents']],
    success: 'Legal hold updated',
  });
  const remove = useApiMutation({
    mutationFn: () => api.del(`/api/incidents/${incidentId}`),
    invalidate: [['incidents']],
    success: 'Moved to trash',
  });
  const restore = act('/restore', 'Incident restored');
  const addNote = useApiMutation({
    mutationFn: () => api.post(`/api/incidents/${incidentId}/notes`, { body: note.trim() }),
    invalidate: [key],
    success: 'Note added',
    onSuccess: () => setNote(''),
  });
  const removeNote = useApiMutation({
    mutationFn: (noteId: string) => api.del(`/api/incidents/${incidentId}/notes/${noteId}`),
    invalidate: [key],
  });
  const detach = useApiMutation({
    mutationFn: (attachmentId: string) =>
      api.del(`/api/incidents/${incidentId}/attachments/${attachmentId}`),
    invalidate: [key],
  });

  if (!incident) return <p className="text-sm text-muted-foreground">Loading…</p>;

  const money = (v: number | null) =>
    v == null ? '—' : `${incident.currency} ${v.toLocaleString(undefined, {
      minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

  return (
    <div className="max-w-4xl space-y-6">
      <div>
        <Link to="/incidents" className="text-sm text-muted-foreground hover:underline">
          ← Incidents
        </Link>
        <div className="mt-1 flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold">{incident.title}</h1>
          <StatusBadge status={incident.status} />
          <SeverityBadge severity={incident.severity} />
          {incident.legalHold && (
            <span className="text-xs font-medium uppercase text-destructive">Legal hold</span>
          )}
          {incident.deletedAt && (
            <span className="text-xs font-medium uppercase text-muted-foreground">In trash</span>
          )}
        </div>
      </div>

      <Card>
        <CardContent className="grid gap-x-8 gap-y-2 pt-4 text-sm sm:grid-cols-2">
          <Field label="Site">{site?.name ?? incident.siteId}</Field>
          <Field label="Category">{categoryLabel(incident.category)}</Field>
          <Field label="Occurred">
            {fmtDateTime(incident.occurredAt)}
            <span className="ml-2 text-muted-foreground">business day {incident.businessDate}</span>
          </Field>
          <Field label="Reported">
            {fmtDateTime(incident.reportedAt)}
            {incident.reporter && <span className="ml-2 text-muted-foreground">by {incident.reporter}</span>}
            <span className="ml-2 text-muted-foreground">via {incident.source}</span>
          </Field>
          <Field label="Where">{incident.locationDetail ?? '—'}</Field>
          <Field label="Police report">{incident.policeReportNumber ?? '—'}</Field>
          <Field label="Loss">{money(incident.lossAmount)}</Field>
          <Field label="Recovered">{money(incident.recoveredAmount)}</Field>
          {incident.tags.length > 0 && (
            <Field label="Tags">{incident.tags.join(', ')}</Field>
          )}
          {incident.closureReason && (
            <Field label="Closed">
              {incident.closedAt && fmtDateTime(incident.closedAt)} · {incident.closureReason}
            </Field>
          )}
          <div className="sm:col-span-2">
            <div className="text-xs uppercase text-muted-foreground">Narrative</div>
            <p className="whitespace-pre-wrap">{incident.narrative || '—'}</p>
          </div>
        </CardContent>
      </Card>

      {manage && (
        <Card>
          <CardHeader><CardTitle className="text-base">Manage</CardTitle></CardHeader>
          <CardContent className="flex flex-wrap items-end gap-3">
            {incident.status === 'Open' ? (
              <div className="flex items-end gap-2">
                <div className="space-y-1">
                  <Label htmlFor="close-reason">Closure reason</Label>
                  <Input id="close-reason" className="w-64" value={reason}
                    placeholder="Unfounded, resolved, referred to police…"
                    onChange={(e) => setReason(e.target.value)} />
                </div>
                <Button disabled={!reason.trim() || close.isPending} onClick={() => close.mutate()}>
                  Close
                </Button>
              </div>
            ) : (
              <Button variant="outline" disabled={reopen.isPending} onClick={() => reopen.mutate()}>
                Reopen
              </Button>
            )}
            <Button variant="outline" disabled={hold.isPending}
              onClick={() => hold.mutate(!incident.legalHold)}>
              {incident.legalHold ? 'Release legal hold' : 'Place legal hold'}
            </Button>
            {incident.deletedAt ? (
              <Button variant="outline" disabled={restore.isPending} onClick={() => restore.mutate()}>
                Restore
              </Button>
            ) : (
              <ConfirmButton variant="destructive" disabled={incident.legalHold || remove.isPending}
                onConfirm={() => remove.mutate()}>
                Move to trash
              </ConfirmButton>
            )}
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center justify-between text-base">
            Evidence
            {report && !incident.deletedAt && <AttachDialog incidentId={incidentId} invalidate={key} />}
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          {incident.attachments.length === 0 && (
            <p className="text-sm text-muted-foreground">No files attached.</p>
          )}
          {incident.attachments.map((a) => (
            <div key={a.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
              <span>
                <span className="font-medium">{a.fileName ?? a.fileId}</span>
                {a.label && <span className="ml-2 text-muted-foreground">{a.label}</span>}
                {a.fileStatus && <span className="ml-2 text-xs text-muted-foreground">{a.fileStatus}</span>}
              </span>
              {manage && !incident.legalHold && (
                <ConfirmButton size="sm" variant="ghost" disabled={detach.isPending}
                  onConfirm={() => detach.mutate(a.id)}>
                  Detach
                </ConfirmButton>
              )}
            </div>
          ))}
        </CardContent>
      </Card>

      {can(me, 'entities:read') && (
        <LinkedEntitiesCard incidentId={incidentId} manage={can(me, 'entities:manage')} />
      )}

      <Card>
        <CardHeader><CardTitle className="text-base">Notes</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          {incident.notes.length === 0 && (
            <p className="text-sm text-muted-foreground">No notes yet.</p>
          )}
          {incident.notes.map((n) => (
            <div key={n.id} className="rounded-md border p-3 text-sm">
              <div className="flex items-center justify-between text-xs text-muted-foreground">
                <span>{n.author ?? 'Unknown'} · {fmtDateTime(n.createdAt)}</span>
                {(manage || (me?.tier === 'user' && n.authorId === me.userId)) && (
                  <button className="hover:underline" disabled={removeNote.isPending}
                    onClick={() => removeNote.mutate(n.id)}>
                    Remove
                  </button>
                )}
              </div>
              <p className="mt-1 whitespace-pre-wrap">{n.body}</p>
            </div>
          ))}
          {report && !incident.deletedAt && (
            <div className="space-y-2">
              <Textarea value={note} rows={3} placeholder="Add a note"
                onChange={(e) => setNote(e.target.value)} />
              <Button size="sm" disabled={!note.trim() || addNote.isPending}
                onClick={() => addNote.mutate()}>
                Add note
              </Button>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="text-xs uppercase text-muted-foreground">{label}</div>
      <div>{children}</div>
    </div>
  );
}

/** Attach a file that already went through the ticket flow on the Files page (ADR 19). */
function AttachDialog({ incidentId, invalidate }: { incidentId: string; invalidate: string[] }) {
  const [open, setOpen] = useState(false);
  const [fileId, setFileId] = useState('');
  const [label, setLabel] = useState('');
  const { data: files } = useQuery({
    queryKey: ['files', 'picker'],
    queryFn: async () => (await api.get<Page<StoredFile>>('/api/files?limit=200')).items,
    enabled: open,
  });
  const attach = useApiMutation({
    mutationFn: () =>
      api.post(`/api/incidents/${incidentId}/attachments`, { fileId, label: label.trim() || null }),
    invalidate: [invalidate],
    success: 'File attached',
    onSuccess: () => {
      setOpen(false);
      setFileId('');
      setLabel('');
    },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Attach evidence"
      description="Upload files on the Files page first, then link them here."
      trigger={<Button size="sm" variant="outline">Attach file</Button>}>
      <div className="space-y-3">
        <div className="space-y-1">
          <Label htmlFor="att-file">File</Label>
          <Select id="att-file" value={fileId} onChange={(e) => setFileId(e.target.value)}>
            <option value="">Choose…</option>
            {files?.map((f) => <option key={f.id} value={f.id}>{f.name} ({f.status})</option>)}
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="att-label">Label</Label>
          <Input id="att-label" value={label} placeholder="Register 3 camera"
            onChange={(e) => setLabel(e.target.value)} />
        </div>
        <Button className="w-full" disabled={!fileId || attach.isPending} onClick={() => attach.mutate()}>
          Attach
        </Button>
      </div>
    </FormDialog>
  );
}

/** The graph's other end: who and what was involved, as far as need-to-know lets this reader see. */
function LinkedEntitiesCard({ incidentId, manage }: { incidentId: string; manage: boolean }) {
  const { data } = useQuery({
    queryKey: ['entities', 'by-incident', incidentId],
    queryFn: () => api.get<Page<EntitySummary>>(`/api/entities?incidentId=${incidentId}&limit=100`),
  });
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between text-base">
          People &amp; vehicles
          {manage && <LinkEntityDialog incidentId={incidentId} />}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {data?.items.length === 0 && (
          <p className="text-sm text-muted-foreground">No records you can see are linked.</p>
        )}
        {data?.items.map((e) => (
          <div key={e.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
            <span>
              <Link to="/entities/$entityId" params={{ entityId: e.id }} className="font-medium hover:underline">
                {e.displayName}
              </Link>
              <span className="ml-2 text-muted-foreground">{e.kind}</span>
            </span>
            <EntityStatusBadge status={e.status} />
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

function LinkEntityDialog({ incidentId }: { incidentId: string }) {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState('');
  const [entityId, setEntityId] = useState('');
  const [role, setRole] = useState<string>('Suspect');
  const { data: entities } = useQuery({
    queryKey: ['entities', 'picker', q],
    queryFn: async () =>
      (await api.get<Page<EntitySummary>>(
        `/api/entities?limit=50${q.trim() ? `&q=${encodeURIComponent(q.trim())}` : ''}`,
      )).items,
    enabled: open,
  });
  const link = useApiMutation({
    mutationFn: () => api.post(`/api/entities/${entityId}/links`, { incidentId, role }),
    invalidate: [['entities']],
    success: 'Record linked',
    onSuccess: () => {
      setOpen(false);
      setEntityId('');
    },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Link a record"
      trigger={<Button size="sm" variant="outline">Link record</Button>}>
      <div className="space-y-3">
        <div className="space-y-1">
          <Label htmlFor="le-q">Find record</Label>
          <Input id="le-q" value={q} placeholder="Name or alias" onChange={(e) => setQ(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="le-ent">Record</Label>
          <Select id="le-ent" value={entityId} onChange={(e) => setEntityId(e.target.value)}>
            <option value="">Choose…</option>
            {entities?.map((e) => <option key={e.id} value={e.id}>{e.displayName} · {e.kind}</option>)}
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="le-role">Role</Label>
          <Select id="le-role" value={role} onChange={(e) => setRole(e.target.value)}>
            {LINK_ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
          </Select>
        </div>
        <Button className="w-full" disabled={!entityId || link.isPending} onClick={() => link.mutate()}>
          Link
        </Button>
      </div>
    </FormDialog>
  );
}
