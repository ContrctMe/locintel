import { enumValue } from '../lib/enum-value';
import { issueEvidenceDownload } from '../features/cases';
import { api, type components } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog,
  Input, Label, Select, Textarea } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDate, fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';

import { useMe } from '../session';
import { StatusBadge } from '../shell';
import { DISPOSITIONS, MEMBER_ROLES, PRIORITIES, PriorityBadge } from './cases';
import { EntityStatusBadge } from './entities';


type CaseDetail = components['schemas']['CaseDetail'];




export function CaseDetailPage() {
  const { caseId } = useParams({ strict: false }) as { caseId: string };
  const { data: me } = useMe();
  const key = ['cases', 'detail', caseId];
  const { data: c } = useQuery({ queryKey: key, queryFn: ({ signal }) => api.get("/api/cases/{id}", { signal, path: { id: caseId } }) });
  const [showCustody, setShowCustody] = useState(false);
  const { data: custody } = useQuery({
    queryKey: ['cases', 'custody', caseId],
    queryFn: ({ signal }) => api.get("/api/cases/{id}/custody", { signal, path: { id: caseId } }),
    enabled: showCustody,
  });
  const [disposition, setDisposition] = useState<string>('Resolved');
  const [closeNote, setCloseNote] = useState('');
  const [note, setNote] = useState('');
  const [taskTitle, setTaskTitle] = useState('');

  const close = useApiMutation({
    mutationFn: () => api.post("/api/cases/{id}/close", { disposition: enumValue(DISPOSITIONS, disposition), note: closeNote.trim() || null }, { path: { id: caseId } }),
    invalidate: [['cases']],
    success: 'Case closed',
  });
  const reopen = useApiMutation({ mutationFn: () => api.post('/api/cases/{id}/reopen', undefined, { path: { id: caseId } }), invalidate: [['cases'], ['files']], success: 'Case reopened' });
  const hold = useApiMutation({
    mutationFn: (value: boolean) => api.post("/api/cases/{id}/hold", { hold: value }, { path: { id: caseId } }),
    invalidate: [['cases'], ['files']],
    success: 'Legal hold updated',
  });
  const remove = useApiMutation({ mutationFn: () => api.del("/api/cases/{id}", { path: { id: caseId } }), invalidate: [['cases']], success: 'Moved to trash' });
  const restore = useApiMutation({ mutationFn: () => api.post('/api/cases/{id}/restore', undefined, { path: { id: caseId } }), invalidate: [['cases'], ['files']], success: 'Case restored' });
  const addNote = useApiMutation({
    mutationFn: () => api.post("/api/cases/{id}/notes", { body: note.trim() }, { path: { id: caseId } }),
    invalidate: [key], success: 'Note added', onSuccess: () => setNote(''),
  });
  const addTask = useApiMutation({
    mutationFn: () => api.post("/api/cases/{id}/tasks", { title: taskTitle.trim() }, { path: { id: caseId } }),
    invalidate: [key], success: 'Task added', onSuccess: () => setTaskTitle(''),
  });
  const toggleTask = useApiMutation({
    mutationFn: (t: { id: string; done: boolean }) => api.post("/api/cases/{id}/tasks/{taskId}/done", { done: t.done }, { path: { id: caseId, taskId: t.id } }),
    invalidate: [key],
  });
  const removeChild = useApiMutation({
    mutationFn: (remove: () => Promise<unknown>) => remove(),
    invalidate: [['cases']],
  });
  const download = useApiMutation({
    mutationFn: (evidenceId: string) =>
      issueEvidenceDownload(caseId, evidenceId),
    invalidate: [['cases', 'custody', caseId], key],
    onSuccess: (r) => window.open(r.url, '_blank', 'noopener'),
  });
  const [brief, setBrief] = useState<{ provider: string; text: string } | null>(null);
  const draftBrief = useApiMutation({
    mutationFn: () => api.post("/api/cases/{id}/assist/brief", undefined, { path: { id: caseId } }),
    onSuccess: (b) => setBrief(b),
    errorFallback: 'Could not draft a brief',
  });
  const fileBrief = useApiMutation({
    mutationFn: () => api.post("/api/cases/{id}/notes", { body: brief!.text }, { path: { id: caseId } }),
    invalidate: [key], success: 'Brief filed as a note', onSuccess: () => setBrief(null),
  });

  if (!c) return <p className="text-sm text-muted-foreground">Loading…</p>;
  const manage = c.canManage;
  const work = c.canWork && !c.deletedAt;

  return (
    <div className="max-w-4xl space-y-6">
      <div>
        <Link to="/cases" className="text-sm text-muted-foreground hover:underline">← Cases</Link>
        <div className="mt-1 flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold">{c.title}</h1>
          <StatusBadge status={c.status} />
          <PriorityBadge priority={c.priority} />
          {c.legalHold && <span className="text-xs font-medium uppercase text-destructive">Legal hold</span>}
          {c.deletedAt && <span className="text-xs font-medium uppercase text-muted-foreground">In trash</span>}
        </div>
        <p className="mt-1 text-sm text-muted-foreground">
          Lead {c.lead ?? '—'} · opened {fmtDate(c.createdAt)}
          {c.disposition && ` · ${c.disposition}${c.closureNote ? `: ${c.closureNote}` : ''}`}
        </p>
        {c.summary && <p className="mt-2 whitespace-pre-wrap text-sm">{c.summary}</p>}
      </div>

      {manage && (
        <Card>
          <CardHeader><CardTitle className="text-base">Manage</CardTitle></CardHeader>
          <CardContent className="flex flex-wrap items-end gap-3">
            {c.status === 'Open' ? (
              <>
                <div className="space-y-1">
                  <Label htmlFor="cl-disp">Disposition</Label>
                  <Select id="cl-disp" className="w-44" value={disposition} onChange={(e) => setDisposition(e.target.value)}>
                    {DISPOSITIONS.map((d) => <option key={d} value={d}>{d}</option>)}
                  </Select>
                </div>
                <div className="space-y-1">
                  <Label htmlFor="cl-note">Closing note</Label>
                  <Input id="cl-note" className="w-56" value={closeNote} onChange={(e) => setCloseNote(e.target.value)} />
                </div>
                <Button disabled={close.isPending} onClick={() => close.mutate()}>Close case</Button>
              </>
            ) : (
              <Button variant="outline" disabled={reopen.isPending} onClick={() => reopen.mutate(undefined)}>Reopen</Button>
            )}
            <EditCaseDialog c={c} />
            <Button variant="outline" disabled={hold.isPending} onClick={() => hold.mutate(!c.legalHold)}>
              {c.legalHold ? 'Release legal hold' : 'Place legal hold'}
            </Button>
            <Link to="/cases/$caseId/package" params={{ caseId }}><Button variant="outline">Print package</Button></Link>
            <Button variant="outline" disabled={draftBrief.isPending} onClick={() => draftBrief.mutate()}>
              {draftBrief.isPending ? 'Drafting…' : 'Draft brief'}
            </Button>
            {c.deletedAt ? (
              <Button variant="outline" disabled={restore.isPending} onClick={() => restore.mutate(undefined)}>Restore</Button>
            ) : (
              <ConfirmButton variant="destructive" disabled={c.legalHold || remove.isPending} onConfirm={() => remove.mutate()}>
                Move to trash
              </ConfirmButton>
            )}
          </CardContent>
        </Card>
      )}

      {brief && (
        <Card className="border-primary/40">
          <CardHeader>
            <CardTitle className="flex items-center justify-between text-base">
              Draft brief <span className="text-xs font-normal text-muted-foreground">{brief.provider} · review before use</span>
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <pre className="whitespace-pre-wrap rounded-md bg-muted p-3 text-sm">{brief.text}</pre>
            <div className="flex gap-2">
              {work && <Button size="sm" disabled={fileBrief.isPending} onClick={() => fileBrief.mutate()}>File as note</Button>}
              <Button size="sm" variant="ghost" onClick={() => setBrief(null)}>Discard</Button>
            </div>
          </CardContent>
        </Card>
      )}

      <div className="grid gap-6 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between text-base">
              Incidents
              {manage && !c.deletedAt && <PickerDialog title="Add incident" kind="incidents" add={(incidentId) => api.post('/api/cases/{id}/incidents', { incidentId }, { path: { id: caseId } })}
                search={(q, signal) => api.get('/api/incidents', { query: { limit: 50, q }, signal })}
                label={(i) => `${i.businessDate} · ${i.title}`} />}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            {c.incidents.length === 0 && <p className="text-sm text-muted-foreground">None yet.</p>}
            {c.incidents.map((i) => (
              <div key={i.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
                <span>
                  <Link to="/incidents/$incidentId" params={{ incidentId: i.incidentId }} className="font-medium hover:underline">
                    {i.title ?? i.incidentId}
                  </Link>
                  {i.occurredAt && <span className="ml-2 text-muted-foreground">{fmtDate(i.occurredAt)}</span>}
                </span>
                {manage && <ConfirmButton size="sm" variant="ghost" onConfirm={() => removeChild.mutate(() => api.del('/api/cases/{id}/incidents/{linkId}', { path: { id: caseId, linkId: i.id } }))}>Remove</ConfirmButton>}
              </div>
            ))}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between text-base">
              People &amp; vehicles
              {manage && !c.deletedAt && <PickerDialog title="Add record" kind="entities" add={(entityId) => api.post('/api/cases/{id}/entities', { entityId }, { path: { id: caseId } })}
                search={(q, signal) => api.get('/api/entities', { query: { limit: 50, q }, signal })}
                label={(e) => `${e.displayName} · ${e.kind}`} />}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            {c.entities.length === 0 && <p className="text-sm text-muted-foreground">None yet.</p>}
            {c.entities.map((e) => (
              <div key={e.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
                {e.restricted ? (
                  <span className="italic text-muted-foreground">Restricted record (need-to-know)</span>
                ) : (
                  <span>
                    <Link to="/entities/$entityId" params={{ entityId: e.entityId! }} className="font-medium hover:underline">
                      {e.displayName}
                    </Link>
                    <span className="ml-2 text-muted-foreground">{e.kind}</span>
                    {e.note && <span className="ml-2 text-muted-foreground">· {e.note}</span>}
                    <span className="ml-2"><EntityStatusBadge status={e.status!} /></span>
                  </span>
                )}
                {manage && <ConfirmButton size="sm" variant="ghost" onConfirm={() => removeChild.mutate(() => api.del('/api/cases/{id}/entities/{linkId}', { path: { id: caseId, linkId: e.id } }))}>Remove</ConfirmButton>}
              </div>
            ))}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between text-base">
              Members
              {manage && !c.deletedAt && <MemberDialog caseId={caseId} />}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            {c.members.map((m) => (
              <div key={m.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
                <span><span className="font-medium">{m.user ?? m.userId}</span><span className="ml-2 text-muted-foreground">{m.role}</span></span>
                {manage && <ConfirmButton size="sm" variant="ghost" onConfirm={() => removeChild.mutate(() => api.del('/api/cases/{id}/members/{memberId}', { path: { id: caseId, memberId: m.id } }))}>Remove</ConfirmButton>}
              </div>
            ))}
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle className="text-base">Tasks</CardTitle></CardHeader>
          <CardContent className="space-y-2">
            {c.tasks.length === 0 && <p className="text-sm text-muted-foreground">No tasks.</p>}
            {c.tasks.map((t) => (
              <label key={t.id} className="flex items-center justify-between gap-3 rounded-md border p-2 text-sm">
                <span className="flex items-center gap-2">
                  <input type="checkbox" className="size-4 accent-primary" checked={!!t.doneAt} disabled={!work}
                    onChange={(e) => toggleTask.mutate({ id: t.id, done: e.target.checked })} />
                  <span className={t.doneAt ? 'text-muted-foreground line-through' : ''}>{t.title}</span>
                  {t.assignee && <span className="text-xs text-muted-foreground">{t.assignee}</span>}
                  {t.dueAt && <span className="text-xs text-muted-foreground">due {fmtDate(t.dueAt)}</span>}
                </span>
                {work && <button className="text-xs hover:underline" onClick={() => removeChild.mutate(() => api.del('/api/cases/{id}/tasks/{taskId}', { path: { id: caseId, taskId: t.id } }))}>Remove</button>}
              </label>
            ))}
            {work && (
              <div className="flex gap-2">
                <Input value={taskTitle} placeholder="New task" onChange={(e) => setTaskTitle(e.target.value)} />
                <Button size="sm" disabled={!taskTitle.trim() || addTask.isPending} onClick={() => addTask.mutate()}>Add</Button>
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center justify-between text-base">
            <span>Evidence <span className="ml-2 text-sm font-normal text-muted-foreground">{c.custodyEvents} custody events</span></span>
            <span className="flex gap-2">
              <Button size="sm" variant="ghost" onClick={() => setShowCustody((v) => !v)}>
                {showCustody ? 'Hide chain' : 'Show chain'}
              </Button>
              {work && <EvidenceDialog caseId={caseId} />}
            </span>
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          {c.evidence.length === 0 && <p className="text-sm text-muted-foreground">No evidence held.</p>}
          {c.evidence.map((e) => (
            <div key={e.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
              <span>
                <span className="font-medium">{e.fileName ?? e.fileId}</span>
                {e.label && <span className="ml-2 text-muted-foreground">{e.label}</span>}
                <span className="ml-2 text-xs text-muted-foreground">added by {e.addedByLabel ?? '—'} {fmtDate(e.addedAt)}</span>
              </span>
              <span className="flex gap-1">
                {work && <Button size="sm" variant="ghost" disabled={download.isPending} onClick={() => download.mutate(e.id)}>Download</Button>}
                {manage && !c.legalHold && <ConfirmButton size="sm" variant="ghost" onConfirm={() => removeChild.mutate(() => api.del('/api/cases/{id}/evidence/{evidenceId}', { path: { id: caseId, evidenceId: e.id } }))}>Remove</ConfirmButton>}
              </span>
            </div>
          ))}
          {showCustody && custody && (
            <div className="mt-3 space-y-1 border-t pt-3 text-xs">
              {custody.events.map((ev) => (
                <div key={ev.id} className="flex gap-3 text-muted-foreground">
                  <span className="w-36 shrink-0">{fmtDateTime(ev.at)}</span>
                  <span className="w-24 shrink-0 font-medium text-foreground">{ev.action}</span>
                  <span>{ev.actor ?? ev.actorTier}{ev.detail ? ` · ${ev.detail}` : ''}</span>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-base">Notes</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          {c.notes.length === 0 && <p className="text-sm text-muted-foreground">No notes yet.</p>}
          {c.notes.map((n) => (
            <div key={n.id} className="rounded-md border p-3 text-sm">
              <div className="flex items-center justify-between text-xs text-muted-foreground">
                <span>{n.author ?? 'Unknown'} · {fmtDateTime(n.createdAt)}</span>
                {(manage || (me?.tier === 'user' && me.userId === n.authorId)) && (
                  <button className="hover:underline" onClick={() => removeChild.mutate(() => api.del('/api/cases/{id}/notes/{noteId}', { path: { id: caseId, noteId: n.id } }))}>Remove</button>
                )}
              </div>
              <p className="mt-1 whitespace-pre-wrap">{n.body}</p>
            </div>
          ))}
          {work && (
            <div className="space-y-2">
              <Textarea value={note} rows={3} placeholder="Add a note" onChange={(e) => setNote(e.target.value)} />
              <Button size="sm" disabled={!note.trim() || addNote.isPending} onClick={() => addNote.mutate()}>Add note</Button>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function EditCaseDialog({ c }: { c: CaseDetail }) {
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState(c.title);
  const [summary, setSummary] = useState(c.summary);
  const [priority, setPriority] = useState<string>(c.priority);
  const [leadId, setLeadId] = useState(c.leadId ?? '');
  const save = useApiMutation({
    mutationFn: () => api.put("/api/cases/{id}", { title: title.trim(), summary: summary.trim() || null, priority: enumValue(PRIORITIES, priority), leadId: leadId || null }, { path: { id: c.id } }),
    invalidate: [['cases']], success: 'Case updated', onSuccess: () => setOpen(false),
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Edit case" trigger={<Button variant="outline">Edit</Button>}>
      <div className="space-y-3">
        <div className="space-y-1"><Label htmlFor="ec-title">Title</Label><Input id="ec-title" value={title} onChange={(e) => setTitle(e.target.value)} /></div>
        <div className="space-y-1">
          <Label htmlFor="ec-priority">Priority</Label>
          <Select id="ec-priority" value={priority} onChange={(e) => setPriority(e.target.value)}>
            {PRIORITIES.map((p) => <option key={p} value={p}>{p}</option>)}
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="ec-lead">Lead (a member)</Label>
          <Select id="ec-lead" value={leadId} onChange={(e) => setLeadId(e.target.value)}>
            <option value="">—</option>
            {c.members.map((m) => <option key={m.userId} value={m.userId}>{m.user ?? m.userId}</option>)}
          </Select>
        </div>
        <div className="space-y-1"><Label htmlFor="ec-summary">Summary</Label><Textarea id="ec-summary" rows={3} value={summary} onChange={(e) => setSummary(e.target.value)} /></div>
        <Button className="w-full" disabled={!title.trim() || save.isPending} onClick={() => save.mutate()}>Save</Button>
      </div>
    </FormDialog>
  );
}

function PickerDialog<T extends { id: string }>({ title, kind, add: addItem, search, label }: {
  title: string; kind: string; add: (id: string) => Promise<unknown>; search: (q: string, signal: AbortSignal) => Promise<{ items: T[] }>; label: (t: T) => string;
}) {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState('');
  const [picked, setPicked] = useState('');
  const { data } = useQuery({ queryKey: ['cases', 'picker', kind, q], queryFn: ({ signal }) => search(q.trim(), signal), enabled: open });
  const add = useApiMutation({
    mutationFn: () => addItem(picked),
    invalidate: [['cases']], success: 'Added', onSuccess: () => { setOpen(false); setPicked(''); },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title={title} trigger={<Button size="sm" variant="outline">{title}</Button>}>
      <div className="space-y-3">
        <Input value={q} placeholder="Search" onChange={(e) => setQ(e.target.value)} />
        <Select value={picked} onChange={(e) => setPicked(e.target.value)}>
          <option value="">Choose…</option>
          {data?.items.map((t) => <option key={t.id} value={t.id}>{label(t)}</option>)}
        </Select>
        <Button className="w-full" disabled={!picked || add.isPending} onClick={() => add.mutate()}>Add</Button>
      </div>
    </FormDialog>
  );
}

function MemberDialog({ caseId }: { caseId: string }) {
  const [open, setOpen] = useState(false);
  const [userId, setUserId] = useState('');
  const [role, setRole] = useState<string>('Investigator');
  const { data: members } = useQuery({
    queryKey: ['members', 'picker'],
    queryFn: async ({ signal }) => { const r = await api.get("/api/members", { signal }); return Array.isArray(r) ? r : r.items; },
    enabled: open,
  });
  const add = useApiMutation({
    mutationFn: () => api.post("/api/cases/{id}/members", { userId, role: enumValue(MEMBER_ROLES, role) }, { path: { id: caseId } }),
    invalidate: [['cases']], success: 'Member added', onSuccess: () => { setOpen(false); setUserId(''); },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Add member" trigger={<Button size="sm" variant="outline">Add member</Button>}>
      <div className="space-y-3">
        <Select value={userId} onChange={(e) => setUserId(e.target.value)}>
          <option value="">Choose…</option>
          {members?.map((m) => <option key={m.userId} value={m.userId}>{m.name ? `${m.name} (${m.email})` : m.email}</option>)}
        </Select>
        <Select value={role} onChange={(e) => setRole(e.target.value)}>
          {MEMBER_ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
        </Select>
        <Button className="w-full" disabled={!userId || add.isPending} onClick={() => add.mutate()}>Add</Button>
      </div>
    </FormDialog>
  );
}

function EvidenceDialog({ caseId }: { caseId: string }) {
  const [open, setOpen] = useState(false);
  const [fileId, setFileId] = useState('');
  const [label, setLabel] = useState('');
  const { data: files } = useQuery({
    queryKey: ['files', 'picker'],
    queryFn: async ({ signal }) => (await api.get('/api/files', { signal, query: { limit: 200 } })).items,
    enabled: open,
  });
  const add = useApiMutation({
    mutationFn: () => api.post("/api/cases/{id}/evidence", { fileId, label: label.trim() || null }, { path: { id: caseId } }),
    invalidate: [['cases']], success: 'Evidence added', onSuccess: () => { setOpen(false); setFileId(''); setLabel(''); },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Add evidence"
      description="Upload on the Files page first; adding here starts the custody chain."
      trigger={<Button size="sm" variant="outline">Add evidence</Button>}>
      <div className="space-y-3">
        <Select value={fileId} onChange={(e) => setFileId(e.target.value)}>
          <option value="">Choose…</option>
          {files?.map((f) => <option key={f.id} value={f.id}>{f.name} ({f.status})</option>)}
        </Select>
        <Input value={label} placeholder="Label" onChange={(e) => setLabel(e.target.value)} />
        <Button className="w-full" disabled={!fileId || add.isPending} onClick={() => add.mutate()}>Add</Button>
      </div>
    </FormDialog>
  );
}
