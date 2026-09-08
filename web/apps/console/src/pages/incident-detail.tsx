import { useSiteMetadata } from '../features/sites';
import { FilePicker } from '../features/files';
import { enumValue } from '../lib/enum-value';
import { api, type components } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog,
  Input, Label, Select, Textarea } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';

import { can, useMe } from '../session';
import { StatusBadge } from '../shell';
import { EntityStatusBadge, LINK_ROLES } from './entities';
import { OpenCaseDialog, PriorityBadge } from './cases';
import { CATEGORIES, SEVERITIES, categoryLabel, SeverityBadge } from '../features/incidents';



type Incident = components['schemas']['IncidentDetail'];



export function IncidentDetailPage() {
  const { incidentId } = useParams({ strict: false }) as { incidentId: string };
  const { data: me } = useMe();
  const manage = can(me, 'incidents:manage');
  const report = can(me, 'incidents:report');
  const key = ['incidents', 'detail', incidentId];
  const { data: incident } = useQuery({
    queryKey: key,
    queryFn: ({ signal }) => api.get("/api/incidents/{id}", { signal, path: { id: incidentId } }),
  });
  const { data: sites } = useSiteMetadata(incident ? [incident.siteId] : []);
  const site = sites?.find((s) => s.id === incident?.siteId);
  const [reason, setReason] = useState('');
  const [note, setNote] = useState('');

  const close = useApiMutation({
    mutationFn: () => api.post("/api/incidents/{id}/close", { reason: reason.trim() }, { path: { id: incidentId } }),
    invalidate: [['incidents']],
    success: 'Incident closed',
    onSuccess: () => setReason(''),
  });
  const reopen = useApiMutation({ mutationFn: () => api.post('/api/incidents/{id}/reopen', undefined, { path: { id: incidentId } }), invalidate: [['incidents']], success: 'Incident reopened' });
  const hold = useApiMutation({
    mutationFn: (value: boolean) =>
      api.post("/api/incidents/{id}/hold", { hold: value }, { path: { id: incidentId } }),
    invalidate: [['incidents']],
    success: 'Legal hold updated',
  });
  const remove = useApiMutation({
    mutationFn: () => api.del("/api/incidents/{id}", { path: { id: incidentId } }),
    invalidate: [['incidents']],
    success: 'Moved to trash',
  });
  const restore = useApiMutation({ mutationFn: () => api.post('/api/incidents/{id}/restore', undefined, { path: { id: incidentId } }), invalidate: [['incidents']], success: 'Incident restored' });
  const addNote = useApiMutation({
    mutationFn: () => api.post("/api/incidents/{id}/notes", { body: note.trim() }, { path: { id: incidentId } }),
    invalidate: [key],
    success: 'Note added',
    onSuccess: () => setNote(''),
  });
  const removeNote = useApiMutation({
    mutationFn: (noteId: string) => api.del("/api/incidents/{id}/notes/{noteId}", { path: { id: incidentId, noteId } }),
    invalidate: [key],
  });
  const detach = useApiMutation({
    mutationFn: (attachmentId: string) =>
      api.del("/api/incidents/{id}/attachments/{attachmentId}", { path: { id: incidentId, attachmentId } }),
    invalidate: [key],
  });

  if (!incident) return <p className="text-sm text-muted-foreground">Loading…</p>;

  const money = (v: number | string | null) =>
    v == null ? '—' : `${incident.currency} ${Number(v).toLocaleString(undefined, {
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
            <EditIncidentDialog incident={incident} />
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

      {can(me, 'cases:read') && <CasesCard incidentId={incidentId} manage={can(me, 'cases:manage')} />}

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
  const attach = useApiMutation({
    mutationFn: () =>
      api.post("/api/incidents/{id}/attachments", { fileId, label: label.trim() || null }, { path: { id: incidentId } }),
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
          <FilePicker id="att-file" value={fileId} onChange={setFileId} />
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
    queryFn: ({ signal }) => api.get('/api/entities', { signal, query: { incidentId, limit: 100 } }),
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
    queryFn: async ({ signal }) =>
      (await api.get('/api/entities', { signal, query: { limit: 50, q: q.trim() || undefined } })).items,
    enabled: open,
  });
  const link = useApiMutation({
    mutationFn: () => api.post("/api/entities/{id}/links", { incidentId, role: enumValue(LINK_ROLES, role) }, { path: { id: entityId } }),
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

function CasesCard({ incidentId, manage }: { incidentId: string; manage: boolean }) {
  const { data } = useQuery({
    queryKey: ['cases', 'by-incident', incidentId],
    queryFn: ({ signal }) => api.get('/api/cases', { signal, query: { incidentId, limit: 50 } }),
  });
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between text-base">
          Cases
          {manage && <OpenCaseDialog incidentId={incidentId} trigger={<Button size="sm" variant="outline">Open case</Button>} />}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {data?.items.length === 0 && <p className="text-sm text-muted-foreground">Not part of a case you can see.</p>}
        {data?.items.map((c) => (
          <div key={c.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
            <Link to="/cases/$caseId" params={{ caseId: c.id }} className="font-medium hover:underline">{c.title}</Link>
            <span className="flex items-center gap-2"><PriorityBadge priority={c.priority} /><StatusBadge status={c.status} /></span>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

type Suggestion = components['schemas']['IncidentAssistResponse'];

/** Edit the incident; "Suggest" asks the assistance port and fills the form - the person still saves. */
function EditIncidentDialog({ incident }: { incident: Incident }) {
  const [open, setOpen] = useState(false);
  const [category, setCategory] = useState<string>(incident.category);
  const [severity, setSeverity] = useState<string>(incident.severity);
  const [title, setTitle] = useState(incident.title);
  const [narrative, setNarrative] = useState(incident.narrative);
  const [locationDetail, setLocationDetail] = useState(incident.locationDetail ?? '');
  const [lossAmount, setLossAmount] = useState(incident.lossAmount == null ? '' : String(incident.lossAmount));
  const [recovered, setRecovered] = useState(incident.recoveredAmount == null ? '' : String(incident.recoveredAmount));
  const [police, setPolice] = useState(incident.policeReportNumber ?? '');
  const [tags, setTags] = useState(incident.tags.join(', '));
  const [suggestion, setSuggestion] = useState<Suggestion | null>(null);
  const suggest = useApiMutation({
    mutationFn: () => api.post("/api/incidents/{id}/assist", undefined, { path: { id: incident.id } }),
    onSuccess: (s) => { setSuggestion(s); setCategory(s.category); setSeverity(s.severity); if (s.tags.length) setTags(s.tags.join(', ')); },
    errorFallback: 'No suggestion available',
  });
  const save = useApiMutation({
    mutationFn: () => api.put("/api/incidents/{id}", {
      category: enumValue(CATEGORIES, category), severity: enumValue(SEVERITIES, severity), title: title.trim(), occurredAt: incident.occurredAt, narrative: narrative.trim() || null,
      locationDetail: locationDetail.trim() || null, lossAmount: lossAmount ? Number(lossAmount) : null,
      recoveredAmount: recovered ? Number(recovered) : null, currency: incident.currency,
      policeReportNumber: police.trim() || null, tags: tags.split(',').map((t) => t.trim()).filter(Boolean),
    }, { path: { id: incident.id } }),
    invalidate: [['incidents']], success: 'Incident updated', onSuccess: () => setOpen(false),
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Edit incident" trigger={<Button variant="outline">Edit</Button>}>
      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <Button size="sm" variant="outline" disabled={suggest.isPending} onClick={() => suggest.mutate()}>
            {suggest.isPending ? 'Thinking…' : 'Suggest category, severity, tags'}
          </Button>
          {suggestion && <span className="text-xs text-muted-foreground">{suggestion.provider} · {Math.round(Number(suggestion.confidence) * 100)}% · {suggestion.summary}</span>}
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1"><Label htmlFor="ei-cat">Category</Label>
            <Select id="ei-cat" value={category} onChange={(e) => setCategory(e.target.value)}>{CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}</Select></div>
          <div className="space-y-1"><Label htmlFor="ei-sev">Severity</Label>
            <Select id="ei-sev" value={severity} onChange={(e) => setSeverity(e.target.value)}>{SEVERITIES.map((s) => <option key={s} value={s}>{s}</option>)}</Select></div>
        </div>
        <div className="space-y-1"><Label htmlFor="ei-title">Title</Label><Input id="ei-title" value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} /></div>
        <div className="space-y-1"><Label htmlFor="ei-narr">Narrative</Label><Textarea id="ei-narr" rows={4} value={narrative} onChange={(e) => setNarrative(e.target.value)} /></div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1"><Label htmlFor="ei-loc">Where in the site</Label><Input id="ei-loc" value={locationDetail} onChange={(e) => setLocationDetail(e.target.value)} /></div>
          <div className="space-y-1"><Label htmlFor="ei-police">Police report</Label><Input id="ei-police" value={police} onChange={(e) => setPolice(e.target.value)} /></div>
          <div className="space-y-1"><Label htmlFor="ei-loss">Loss</Label><Input id="ei-loss" type="number" min="0" step="0.01" value={lossAmount} onChange={(e) => setLossAmount(e.target.value)} /></div>
          <div className="space-y-1"><Label htmlFor="ei-rec">Recovered</Label><Input id="ei-rec" type="number" min="0" step="0.01" value={recovered} onChange={(e) => setRecovered(e.target.value)} /></div>
        </div>
        <div className="space-y-1"><Label htmlFor="ei-tags">Tags (comma separated)</Label><Input id="ei-tags" value={tags} onChange={(e) => setTags(e.target.value)} /></div>
        <Button className="w-full" disabled={!title.trim() || save.isPending} onClick={() => save.mutate()}>Save</Button>
      </div>
    </FormDialog>
  );
}
