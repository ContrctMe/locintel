import { enumValue } from '../lib/enum-value';
import { api } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog, Input, Label, Select, Textarea } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';

import { useState } from 'react';
import { fmtDate } from '../lib/format';
import { useApiMutation } from '../lib/mutation';

import { can, useMe } from '../session';

import { SeverityBadge } from '../features/incidents';





/** Cross-org intelligence sharing (blueprint module 9): shares you are in, what flows through them. */
export function NetworkPage() {
  const { data: me } = useMe();
  const manage = can(me, 'network:manage');
  const [selected, setSelected] = useState('');
  const [q, setQ] = useState('');
  const { data: shares } = useQuery({ queryKey: ['network', 'shares'], queryFn: ({ signal }) => api.get("/api/network/shares", { signal }) });
  const activeShare = shares?.items.find((s) => s.id === selected) ?? null;
  const { data: detail } = useQuery({
    queryKey: ['network', 'share', selected],
    queryFn: ({ signal }) => api.get("/api/network/shares/{id}", { signal, path: { id: selected } }),
    enabled: !!selected,
  });
  const params = new URLSearchParams({ limit: '100' });
  if (selected) params.set('shareId', selected);
  if (q.trim()) params.set('q', q.trim());
  const { data: bulletins } = useQuery({
    queryKey: ['network', 'bulletins', params.toString()],
    queryFn: ({ signal }) => api.get('/api/network/bulletins', { signal, query: { limit: 100, shareId: selected || undefined, q: q.trim() || undefined } }),
  });
  const accept = useApiMutation({ mutationFn: (id: string) => api.post("/api/network/shares/{id}/accept", undefined, { path: { id } }), invalidate: [['network']], success: 'Joined' });
  const leave = useApiMutation({ mutationFn: (id: string) => api.post("/api/network/shares/{id}/leave", undefined, { path: { id } }), invalidate: [['network']], success: 'Left the share' });
  const remove = useApiMutation({ mutationFn: (i: { id: string; orgId: string }) => api.del("/api/network/shares/{id}/members/{orgId}", { path: { id: i.id, orgId: i.orgId } }), invalidate: [['network']], success: 'Removed' });
  const withdraw = useApiMutation({ mutationFn: (id: string) => api.post("/api/network/bulletins/{id}/withdraw", undefined, { path: { id } }), invalidate: [['network']], success: 'Withdrawn' });
  const importOne = useApiMutation({ mutationFn: (id: string) => api.post("/api/network/bulletins/{id}/import", undefined, { path: { id } }), invalidate: [['entities']], success: 'Import queued; it will appear under People & vehicles shortly' });

  return (
    <div className="max-w-5xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Network</h1>
        {manage && <CreateShareDialog />}
      </div>
      <p className="text-sm text-muted-foreground">
        Shares are consortiums of organizations. What you publish into one is a copy; your own records stay yours.
      </p>
      <div className="grid gap-6 md:grid-cols-3">
        <Card className="md:col-span-1">
          <CardHeader><CardTitle className="text-base">Shares</CardTitle></CardHeader>
          <CardContent className="space-y-2">
            <button className={`w-full rounded-md border p-2 text-left text-sm ${selected ? '' : 'border-primary'}`} onClick={() => setSelected('')}>All shares</button>
            {shares?.items.length === 0 && <p className="text-sm text-muted-foreground">Not in any share yet.</p>}
            {shares?.items.map((s) => (
              <div key={s.id} className={`rounded-md border p-2 text-sm ${selected === s.id ? 'border-primary' : ''}`}>
                <button className="w-full text-left" onClick={() => setSelected(s.id)}>
                  <div className="font-medium">{s.name}</div>
                  <div className="text-xs text-muted-foreground">{s.ownerName} · {s.activeMembers} orgs · {s.activeBulletins} active</div>
                </button>
                {s.membership === 'Invited' && manage && (
                  <Button size="sm" className="mt-2 w-full" onClick={() => accept.mutate(s.id)}>Accept invitation</Button>
                )}
              </div>
            ))}
          </CardContent>
        </Card>

        <div className="space-y-6 md:col-span-2">
          {activeShare && detail && (
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center justify-between text-base">
                  {activeShare.name}
                  <span className="flex gap-2">
                    {manage && activeShare.owned && <InviteDialog shareId={activeShare.id} />}
                    {manage && activeShare.membership === 'Active' && <PublishDialog shareId={activeShare.id} />}
                    {manage && !activeShare.owned && activeShare.membership === 'Active' && (
                      <ConfirmButton size="sm" variant="ghost" onConfirm={() => leave.mutate(activeShare.id)}>Leave</ConfirmButton>
                    )}
                  </span>
                </CardTitle>
              </CardHeader>
              <CardContent className="space-y-2 text-sm">
                {activeShare.description && <p className="text-muted-foreground">{activeShare.description}</p>}
                <div className="text-xs uppercase text-muted-foreground">Members</div>
                {detail.members.map((m) => (
                  <div key={m.orgId} className="flex items-center justify-between rounded-md border p-2">
                    <span>{m.orgName} <span className="ml-2 text-xs text-muted-foreground">{m.role} · {m.status}</span></span>
                    {manage && activeShare.owned && m.role !== 'Owner' && m.status !== 'Removed' && (
                      <ConfirmButton size="sm" variant="ghost" onConfirm={() => remove.mutate({ id: activeShare.id, orgId: m.orgId })}>Remove</ConfirmButton>
                    )}
                  </div>
                ))}
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader>
              <CardTitle className="flex items-center justify-between text-base">
                Shared bulletins
                <Input className="w-48" placeholder="Search" value={q} onChange={(e) => setQ(e.target.value)} />
              </CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              {bulletins?.items.length === 0 && <p className="text-sm text-muted-foreground">Nothing shared here yet.</p>}
              {bulletins?.items.map((b) => (
                <div key={b.id} className="rounded-md border p-3 text-sm">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">{b.kind}</span>
                    <SeverityBadge severity={b.severity} />
                    <span className="font-medium">{b.title}</span>
                    <span className="text-xs text-muted-foreground">from {b.publisherName} in {b.shareName}</span>
                  </div>
                  <p className="mt-1 whitespace-pre-wrap">{b.body}</p>
                  {b.displayName && (
                    <div className="mt-2 rounded bg-muted p-2 text-xs">
                      <span className="font-medium">{b.entityKind}: {b.displayName}</span>
                      {b.aliases.length > 0 && <span className="ml-2 text-muted-foreground">aka {b.aliases.join(', ')}</span>}
                      {Object.entries(b.descriptors).length > 0 && (
                        <span className="ml-2 text-muted-foreground">{Object.entries(b.descriptors).map(([k, v]) => `${k}: ${v}`).join(' · ')}</span>
                      )}
                    </div>
                  )}
                  <div className="mt-2 flex items-center justify-between text-xs text-muted-foreground">
                    <span>{fmtDate(b.publishedAt)} → {fmtDate(b.expiresAt)}{b.areas.length > 0 && ` · ${b.areas.join(', ')}`}</span>
                    <span className="flex gap-1">
                      {manage && !b.mine && b.displayName && <Button size="sm" variant="outline" onClick={() => importOne.mutate(b.id)}>Import record</Button>}
                      {manage && b.mine && <Button size="sm" variant="ghost" onClick={() => withdraw.mutate(b.id)}>Withdraw</Button>}
                    </span>
                  </div>
                </div>
              ))}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}

function CreateShareDialog() {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const create = useApiMutation({
    mutationFn: () => api.post('/api/network/shares', { name: name.trim(), description: description.trim() || null }),
    invalidate: [['network']], success: 'Share created', onSuccess: () => { setOpen(false); setName(''); setDescription(''); },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="New share" description="Your org owns it and invites others by their slug." trigger={<Button>New share</Button>}>
      <div className="space-y-3">
        <div className="space-y-1"><Label htmlFor="sh-name">Name</Label><Input id="sh-name" value={name} onChange={(e) => setName(e.target.value)} /></div>
        <div className="space-y-1"><Label htmlFor="sh-desc">Description</Label><Textarea id="sh-desc" rows={2} value={description} onChange={(e) => setDescription(e.target.value)} /></div>
        <Button className="w-full" disabled={!name.trim() || create.isPending} onClick={() => create.mutate()}>Create</Button>
      </div>
    </FormDialog>
  );
}

function InviteDialog({ shareId }: { shareId: string }) {
  const [open, setOpen] = useState(false);
  const [slug, setSlug] = useState('');
  const invite = useApiMutation({
    mutationFn: () => api.post("/api/network/shares/{id}/invite", { slug: slug.trim() }, { path: { id: shareId } }),
    invalidate: [['network']], success: 'Invitation sent', onSuccess: () => { setOpen(false); setSlug(''); },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Invite an organization" description="Use their public slug (the subdomain of their public site)." trigger={<Button size="sm" variant="outline">Invite</Button>}>
      <div className="space-y-3">
        <Input value={slug} placeholder="acme" onChange={(e) => setSlug(e.target.value)} />
        <Button className="w-full" disabled={!slug.trim() || invite.isPending} onClick={() => invite.mutate()}>Invite</Button>
      </div>
    </FormDialog>
  );
}

function PublishDialog({ shareId }: { shareId: string }) {
  const [open, setOpen] = useState(false);
  const [kind, setKind] = useState('Bolo');
  const [severity, setSeverity] = useState('High');
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [entityId, setEntityId] = useState('');
  const [q, setQ] = useState('');
  const [areas, setAreas] = useState('');
  const { data: entities } = useQuery({
    queryKey: ['entities', 'picker', q],
    queryFn: async ({ signal }) => (await api.get('/api/entities', { signal, query: { limit: 50, q: q.trim() || undefined } })).items,
    enabled: open,
  });
  const publish = useApiMutation({
    mutationFn: () => api.post("/api/network/shares/{id}/bulletins", {
      kind: enumValue(['Advisory', 'Bolo'], kind), severity: enumValue(['Low', 'Medium', 'High', 'Critical'], severity), title: title.trim(), body: body.trim(), entityId: entityId || null,
      areas: areas.split(',').map((a) => a.trim()).filter(Boolean),
    }, { path: { id: shareId } }),
    invalidate: [['network']], success: 'Published to the share', onSuccess: () => { setOpen(false); setTitle(''); setBody(''); setEntityId(''); },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Publish to the share"
      description="A copy goes to every active member. Attach one of your records to share its description."
      trigger={<Button size="sm">Publish</Button>}>
      <div className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1"><Label htmlFor="pb-kind">Kind</Label><Select id="pb-kind" value={kind} onChange={(e) => setKind(e.target.value)}><option>Bolo</option><option>Advisory</option></Select></div>
          <div className="space-y-1"><Label htmlFor="pb-sev">Severity</Label><Select id="pb-sev" value={severity} onChange={(e) => setSeverity(e.target.value)}><option>Low</option><option>Medium</option><option>High</option><option>Critical</option></Select></div>
        </div>
        <div className="space-y-1"><Label htmlFor="pb-title">Title</Label><Input id="pb-title" value={title} onChange={(e) => setTitle(e.target.value)} /></div>
        <div className="space-y-1"><Label htmlFor="pb-body">Body</Label><Textarea id="pb-body" rows={3} value={body} onChange={(e) => setBody(e.target.value)} /></div>
        <div className="space-y-1">
          <Label htmlFor="pb-q">Attach a record (optional)</Label>
          <Input id="pb-q" value={q} placeholder="Search your records" onChange={(e) => setQ(e.target.value)} />
          <Select value={entityId} onChange={(e) => setEntityId(e.target.value)}>
            <option value="">None</option>
            {entities?.map((e) => <option key={e.id} value={e.id}>{e.displayName} · {e.kind}</option>)}
          </Select>
        </div>
        <div className="space-y-1"><Label htmlFor="pb-areas">Areas (codes, comma separated)</Label><Input id="pb-areas" value={areas} placeholder="CA, NV" onChange={(e) => setAreas(e.target.value)} /></div>
        <Button className="w-full" disabled={!title.trim() || !body.trim() || publish.isPending} onClick={() => publish.mutate()}>Publish</Button>
      </div>
    </FormDialog>
  );
}
