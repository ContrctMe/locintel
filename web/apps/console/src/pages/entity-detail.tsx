import { api } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog,
  Input, Label, Select, Textarea } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDate, fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import type { Page } from '../lib/paging';
import { can, useMe } from '../session';
import { ENTITY_STATUSES, EntityStatusBadge, LINK_ROLES, parseDescriptors } from './entities';
import type { IncidentSummary } from './incidents';

type LinkView = {
  id: string; incidentId: string; siteId: string; incidentTitle: string | null;
  incidentStatus: string | null; occurredAt: string | null; role: string; note: string | null;
  linkedAt: string;
};
type GrantView = {
  id: string; userId: string; user: string | null; reason: string; expiresAt: string; createdAt: string;
};
type Entity = {
  id: string; kind: string; status: string; displayName: string; aliases: string[];
  descriptors: Record<string, string>; summary: string; expiresAt: string; legalHold: boolean;
  creator: string | null; createdAt: string; updatedAt: string; deletedAt: string | null;
  links: LinkView[]; grants: GrantView[] | null;
};
type Member = { userId: string; email: string; name: string | null };

export function EntityDetailPage() {
  const { entityId } = useParams({ strict: false }) as { entityId: string };
  const { data: me } = useMe();
  const manage = can(me, 'entities:manage');
  const key = ['entities', 'detail', entityId];
  const { data: entity } = useQuery({
    queryKey: key,
    queryFn: () => api.get<Entity>(`/api/entities/${entityId}`),
  });
  const [editing, setEditing] = useState(false);

  const setStatus = useApiMutation({
    mutationFn: (status: string) => api.post(`/api/entities/${entityId}/status`, { status }),
    invalidate: [['entities']],
    success: 'Status updated',
  });
  const hold = useApiMutation({
    mutationFn: (value: boolean) => api.post(`/api/entities/${entityId}/hold`, { hold: value }),
    invalidate: [['entities']],
    success: 'Legal hold updated',
  });
  const remove = useApiMutation({
    mutationFn: () => api.del(`/api/entities/${entityId}`),
    invalidate: [['entities']],
    success: 'Moved to trash',
  });
  const restore = useApiMutation({
    mutationFn: () => api.post(`/api/entities/${entityId}/restore`),
    invalidate: [['entities']],
    success: 'Record restored',
  });
  const unlink = useApiMutation({
    mutationFn: (linkId: string) => api.del(`/api/entities/${entityId}/links/${linkId}`),
    invalidate: [['entities'], ['incidents']],
  });
  const revoke = useApiMutation({
    mutationFn: (grantId: string) => api.del(`/api/entities/${entityId}/grants/${grantId}`),
    invalidate: [key],
    success: 'Access revoked',
  });

  if (!entity) return <p className="text-sm text-muted-foreground">Loading…</p>;

  return (
    <div className="max-w-4xl space-y-6">
      <div>
        <Link to="/entities" className="text-sm text-muted-foreground hover:underline">
          ← People &amp; vehicles
        </Link>
        <div className="mt-1 flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold">{entity.displayName}</h1>
          <span className="text-sm text-muted-foreground">{entity.kind}</span>
          <EntityStatusBadge status={entity.status} />
          {entity.legalHold && (
            <span className="text-xs font-medium uppercase text-destructive">Legal hold</span>
          )}
          {entity.deletedAt && (
            <span className="text-xs font-medium uppercase text-muted-foreground">In trash</span>
          )}
        </div>
      </div>

      <Card>
        <CardContent className="grid gap-x-8 gap-y-2 pt-4 text-sm sm:grid-cols-2">
          <Field label="Aliases">{entity.aliases.length ? entity.aliases.join(', ') : '—'}</Field>
          <Field label="Retention until">{fmtDate(entity.expiresAt)}</Field>
          <Field label="Created">
            {fmtDateTime(entity.createdAt)}
            {entity.creator && <span className="ml-2 text-muted-foreground">by {entity.creator}</span>}
          </Field>
          <Field label="Updated">{fmtDateTime(entity.updatedAt)}</Field>
          <div className="sm:col-span-2">
            <div className="text-xs uppercase text-muted-foreground">Descriptors</div>
            {Object.keys(entity.descriptors).length === 0 ? (
              <p>—</p>
            ) : (
              <dl className="grid grid-cols-2 gap-x-4 gap-y-1 sm:grid-cols-3">
                {Object.entries(entity.descriptors).map(([k, v]) => (
                  <div key={k}>
                    <dt className="text-xs text-muted-foreground">{k}</dt>
                    <dd>{v}</dd>
                  </div>
                ))}
              </dl>
            )}
          </div>
          <div className="sm:col-span-2">
            <div className="text-xs uppercase text-muted-foreground">Why this record exists</div>
            <p className="whitespace-pre-wrap">{entity.summary || '—'}</p>
          </div>
        </CardContent>
      </Card>

      {manage && (
        <Card>
          <CardHeader><CardTitle className="text-base">Manage</CardTitle></CardHeader>
          <CardContent className="flex flex-wrap items-end gap-3">
            <div className="space-y-1">
              <Label htmlFor="ent-status">Status</Label>
              <Select id="ent-status" className="w-40" value={entity.status}
                disabled={setStatus.isPending}
                onChange={(e) => setStatus.mutate(e.target.value)}>
                {ENTITY_STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
              </Select>
            </div>
            <EditDialog entity={entity} open={editing} onOpenChange={setEditing} />
            <Button variant="outline" disabled={hold.isPending}
              onClick={() => hold.mutate(!entity.legalHold)}>
              {entity.legalHold ? 'Release legal hold' : 'Place legal hold'}
            </Button>
            {entity.deletedAt ? (
              <Button variant="outline" disabled={restore.isPending} onClick={() => restore.mutate()}>
                Restore
              </Button>
            ) : (
              <ConfirmButton variant="destructive" disabled={entity.legalHold || remove.isPending}
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
            Linked incidents
            {manage && !entity.deletedAt && <LinkIncidentDialog entityId={entityId} />}
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          {entity.links.length === 0 && (
            <p className="text-sm text-muted-foreground">
              No incidents you can see are linked. Confirming a record requires at least one.
            </p>
          )}
          {entity.links.map((l) => (
            <div key={l.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
              <span>
                <Link to="/incidents/$incidentId" params={{ incidentId: l.incidentId }}
                  className="font-medium hover:underline">
                  {l.incidentTitle ?? l.incidentId}
                </Link>
                <span className="ml-2 text-muted-foreground">{l.role}</span>
                {l.occurredAt && <span className="ml-2 text-muted-foreground">{fmtDate(l.occurredAt)}</span>}
                {l.note && <span className="ml-2 text-muted-foreground">· {l.note}</span>}
              </span>
              {manage && (
                <ConfirmButton size="sm" variant="ghost" disabled={unlink.isPending}
                  onConfirm={() => unlink.mutate(l.id)}>
                  Unlink
                </ConfirmButton>
              )}
            </div>
          ))}
        </CardContent>
      </Card>

      {manage && entity.grants && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between text-base">
              Need-to-know grants
              {!entity.deletedAt && <GrantDialog entityId={entityId} invalidate={key} />}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            <p className="text-sm text-muted-foreground">
              People outside this record's incident scope see it only through a grant. Each grant is
              logged with its reason and expires on its own.
            </p>
            {entity.grants.map((g) => (
              <div key={g.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
                <span>
                  <span className="font-medium">{g.user ?? g.userId}</span>
                  <span className="ml-2 text-muted-foreground">{g.reason}</span>
                  <span className="ml-2 text-xs text-muted-foreground">until {fmtDate(g.expiresAt)}</span>
                </span>
                <ConfirmButton size="sm" variant="ghost" disabled={revoke.isPending}
                  onConfirm={() => revoke.mutate(g.id)}>
                  Revoke
                </ConfirmButton>
              </div>
            ))}
          </CardContent>
        </Card>
      )}
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

function EditDialog({ entity, open, onOpenChange }: {
  entity: Entity; open: boolean; onOpenChange: (open: boolean) => void;
}) {
  const [displayName, setDisplayName] = useState(entity.displayName);
  const [aliases, setAliases] = useState(entity.aliases.join(', '));
  const [descriptors, setDescriptors] = useState(
    Object.entries(entity.descriptors).map(([k, v]) => `${k}: ${v}`).join('\n'),
  );
  const [summary, setSummary] = useState(entity.summary);
  const [expiresAt, setExpiresAt] = useState(entity.expiresAt.slice(0, 10));
  const save = useApiMutation({
    mutationFn: () =>
      api.put(`/api/entities/${entity.id}`, {
        displayName: displayName.trim(),
        aliases: aliases.split(',').map((a) => a.trim()).filter(Boolean),
        descriptors: parseDescriptors(descriptors),
        summary: summary.trim() || null,
        expiresAt: new Date(`${expiresAt}T00:00:00Z`).toISOString(),
      }),
    invalidate: [['entities']],
    success: 'Record updated',
    onSuccess: () => onOpenChange(false),
  });
  return (
    <FormDialog open={open} onOpenChange={onOpenChange} title="Edit record"
      trigger={<Button variant="outline">Edit</Button>}>
      <div className="space-y-3">
        <div className="space-y-1">
          <Label htmlFor="ed-name">Display name</Label>
          <Input id="ed-name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="ed-aliases">Aliases (comma separated)</Label>
          <Input id="ed-aliases" value={aliases} onChange={(e) => setAliases(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="ed-desc">Descriptors, one per line as key: value</Label>
          <Textarea id="ed-desc" rows={3} value={descriptors}
            onChange={(e) => setDescriptors(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="ed-summary">Why this record exists</Label>
          <Textarea id="ed-summary" rows={3} value={summary} onChange={(e) => setSummary(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="ed-expires">Retention until (max three years)</Label>
          <Input id="ed-expires" type="date" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} />
        </div>
        <Button className="w-full" disabled={!displayName.trim() || save.isPending}
          onClick={() => save.mutate()}>
          Save
        </Button>
      </div>
    </FormDialog>
  );
}

function LinkIncidentDialog({ entityId }: { entityId: string }) {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState('');
  const [incidentId, setIncidentId] = useState('');
  const [role, setRole] = useState<string>('Suspect');
  const [note, setNote] = useState('');
  const { data: incidents } = useQuery({
    queryKey: ['incidents', 'picker', q],
    queryFn: async () =>
      (await api.get<Page<IncidentSummary>>(
        `/api/incidents?limit=50${q.trim() ? `&q=${encodeURIComponent(q.trim())}` : ''}`,
      )).items,
    enabled: open,
  });
  const link = useApiMutation({
    mutationFn: () =>
      api.post(`/api/entities/${entityId}/links`, { incidentId, role, note: note.trim() || null }),
    invalidate: [['entities'], ['incidents']],
    success: 'Incident linked',
    onSuccess: () => {
      setOpen(false);
      setIncidentId('');
      setNote('');
    },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Link an incident"
      description="The link stamps the incident's location, which is what need-to-know reads."
      trigger={<Button size="sm" variant="outline">Link incident</Button>}>
      <div className="space-y-3">
        <div className="space-y-1">
          <Label htmlFor="ln-q">Find incident</Label>
          <Input id="ln-q" value={q} placeholder="Search title" onChange={(e) => setQ(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="ln-inc">Incident</Label>
          <Select id="ln-inc" value={incidentId} onChange={(e) => setIncidentId(e.target.value)}>
            <option value="">Choose…</option>
            {incidents?.map((i) => (
              <option key={i.id} value={i.id}>{i.businessDate} · {i.title}</option>
            ))}
          </Select>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1">
            <Label htmlFor="ln-role">Role</Label>
            <Select id="ln-role" value={role} onChange={(e) => setRole(e.target.value)}>
              {LINK_ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
            </Select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="ln-note">Note</Label>
            <Input id="ln-note" value={note} onChange={(e) => setNote(e.target.value)} />
          </div>
        </div>
        <Button className="w-full" disabled={!incidentId || link.isPending} onClick={() => link.mutate()}>
          Link
        </Button>
      </div>
    </FormDialog>
  );
}

function GrantDialog({ entityId, invalidate }: { entityId: string; invalidate: string[] }) {
  const [open, setOpen] = useState(false);
  const [userId, setUserId] = useState('');
  const [reason, setReason] = useState('');
  const [days, setDays] = useState('7');
  const { data: members } = useQuery({
    queryKey: ['members', 'picker'],
    queryFn: async () => {
      const result = await api.get<Member[] | Page<Member>>('/api/members');
      return Array.isArray(result) ? result : result.items;
    },
    enabled: open,
  });
  const grant = useApiMutation({
    mutationFn: () =>
      api.post(`/api/entities/${entityId}/grants`, {
        userId,
        reason: reason.trim(),
        expiresAt: new Date(Date.now() + Number(days) * 86_400_000).toISOString(),
      }),
    invalidate: [invalidate],
    success: 'Access granted',
    onSuccess: () => {
      setOpen(false);
      setUserId('');
      setReason('');
    },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Grant access"
      description="Share this one record with one person, for a reason, for up to 90 days."
      trigger={<Button size="sm" variant="outline">Grant access</Button>}>
      <div className="space-y-3">
        <div className="space-y-1">
          <Label htmlFor="gr-user">Member</Label>
          <Select id="gr-user" value={userId} onChange={(e) => setUserId(e.target.value)}>
            <option value="">Choose…</option>
            {members?.map((m) => (
              <option key={m.userId} value={m.userId}>{m.name ? `${m.name} (${m.email})` : m.email}</option>
            ))}
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="gr-reason">Reason</Label>
          <Input id="gr-reason" value={reason} onChange={(e) => setReason(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="gr-days">Days</Label>
          <Input id="gr-days" type="number" min="1" max="90" value={days}
            onChange={(e) => setDays(e.target.value)} />
        </div>
        <Button className="w-full" disabled={!userId || !reason.trim() || grant.isPending}
          onClick={() => grant.mutate()}>
          Grant
        </Button>
      </div>
    </FormDialog>
  );
}
