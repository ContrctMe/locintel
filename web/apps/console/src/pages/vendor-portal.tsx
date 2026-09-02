import { api } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, Input, Label, Select, Table,
  TableBody, TableCell, TableHead, TableHeader, TableRow, Textarea } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDate, fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import type { Page } from '../lib/paging';
import { can, useMe } from '../session';
import { CATEGORIES, categoryLabel, RequestStatusBadge, type RequestSummary } from './marketplace';
import { RequestFacts, Timeline, type RequestDetail } from './request-detail';

type Profile = {
  orgId: string; updatedAt: string; name: string; description: string; categories: string[]; serviceAreas: string[];
  latitude: number | null; longitude: number | null; serviceRadiusKm: number | null;
  contactEmail: string | null; contactPhone: string | null; published: boolean;
  credentials: { id: string; kind: string; label: string; number: string | null; jurisdiction: string | null; expiresAt: string; expired: boolean }[];
};

/** The seller's side: profile, credentials, and the queue of requests addressed to this org. */
export function VendorPortalPage() {
  const { data: me } = useMe();
  const manage = can(me, 'vendor:manage');
  const { data: profile, isError, isPending } = useQuery({
    queryKey: ['vendor', 'profile'],
    queryFn: () => api.get<Profile>('/api/vendor/profile'),
    retry: false,
  });
  const { data: queue } = useQuery({
    queryKey: ['vendor', 'requests'],
    queryFn: () => api.get<Page<RequestSummary>>('/api/vendor/requests?limit=100'),
  });
  return (
    <div className="max-w-5xl space-y-6">
      <h1 className="text-2xl font-semibold">Vendor portal</h1>
      <p className="text-sm text-muted-foreground">
        Your organization as a fulfillment vendor. Publish a profile to appear in every buyer's catalog; requests they send land below.
      </p>
      {manage && !isPending && (
        <ProfileCard key={profile?.updatedAt ?? 'new'} profile={isError ? null : (profile ?? null)} />
      )}
      <Card>
        <CardHeader><CardTitle className="text-base">Incoming requests</CardTitle></CardHeader>
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow><TableHead>Request</TableHead><TableHead>From</TableHead><TableHead>Site</TableHead><TableHead>Urgency</TableHead><TableHead>Status</TableHead><TableHead>Starts</TableHead></TableRow>
            </TableHeader>
            <TableBody>
              {queue?.items.length === 0 && <TableRow><TableCell colSpan={6} className="text-center text-sm text-muted-foreground">Nothing yet.</TableCell></TableRow>}
              {queue?.items.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>
                    <Link to="/vendor/requests/$requestId" params={{ requestId: r.id }} className="font-medium hover:underline">{r.title}</Link>
                    <div className="text-xs text-muted-foreground">{categoryLabel(r.category)}</div>
                  </TableCell>
                  <TableCell className="text-sm">{r.requesterName}</TableCell>
                  <TableCell className="text-sm">{r.siteName}</TableCell>
                  <TableCell className="text-sm">{r.urgency}</TableCell>
                  <TableCell><RequestStatusBadge status={r.status} /></TableCell>
                  <TableCell className="text-sm">{fmtDateTime(r.startsAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}

function ProfileCard({ profile }: { profile: Profile | null }) {
  const [name, setName] = useState(profile?.name ?? '');
  const [description, setDescription] = useState(profile?.description ?? '');
  const [categories, setCategories] = useState<string[]>(profile?.categories ?? []);
  const [areas, setAreas] = useState(profile?.serviceAreas.join(', ') ?? '');
  const [email, setEmail] = useState(profile?.contactEmail ?? '');
  const [phone, setPhone] = useState(profile?.contactPhone ?? '');
  const [credKind, setCredKind] = useState('License');
  const [credLabel, setCredLabel] = useState('');
  const [credExpires, setCredExpires] = useState('');
  const save = useApiMutation({
    mutationFn: () => api.put('/api/vendor/profile', {
      name: name.trim(), description: description.trim() || null, categories,
      serviceAreas: areas.split(',').map((a) => a.trim()).filter(Boolean),
      latitude: profile?.latitude ?? null, longitude: profile?.longitude ?? null, serviceRadiusKm: profile?.serviceRadiusKm ?? null,
      contactEmail: email.trim() || null, contactPhone: phone.trim() || null,
    }),
    invalidate: [['vendor']], success: 'Profile saved',
  });
  const publish = useApiMutation({
    mutationFn: (published: boolean) => api.post('/api/vendor/profile/publish', { published }),
    invalidate: [['vendor']], success: 'Visibility updated',
  });
  const addCred = useApiMutation({
    mutationFn: () => api.post('/api/vendor/credentials', { kind: credKind, label: credLabel.trim(), expiresAt: new Date(`${credExpires}T00:00:00Z`).toISOString() }),
    invalidate: [['vendor']], success: 'Credential added', onSuccess: () => { setCredLabel(''); setCredExpires(''); },
  });
  const removeCred = useApiMutation({ mutationFn: (id: string) => api.del(`/api/vendor/credentials/${id}`), invalidate: [['vendor']] });
  const toggle = (c: string) => setCategories((cs) => (cs.includes(c) ? cs.filter((x) => x !== c) : [...cs, c]));
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between text-base">
          Profile
          {profile && (
            <Button size="sm" variant={profile.published ? 'outline' : 'default'} onClick={() => publish.mutate(!profile.published)}>
              {profile.published ? 'Unpublish' : 'Publish to catalog'}
            </Button>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1"><Label htmlFor="vp-name">Name</Label><Input id="vp-name" value={name} onChange={(e) => setName(e.target.value)} /></div>
          <div className="space-y-1"><Label htmlFor="vp-areas">Service areas (codes, comma separated)</Label><Input id="vp-areas" value={areas} placeholder="CA, NV" onChange={(e) => setAreas(e.target.value)} /></div>
          <div className="space-y-1"><Label htmlFor="vp-email">Dispatch email</Label><Input id="vp-email" value={email} onChange={(e) => setEmail(e.target.value)} /></div>
          <div className="space-y-1"><Label htmlFor="vp-phone">Dispatch phone</Label><Input id="vp-phone" value={phone} onChange={(e) => setPhone(e.target.value)} /></div>
        </div>
        <div className="space-y-1"><Label htmlFor="vp-desc">Description</Label><Textarea id="vp-desc" rows={2} value={description} onChange={(e) => setDescription(e.target.value)} /></div>
        <div>
          <Label>Categories offered</Label>
          <div className="mt-1 flex flex-wrap gap-2">
            {CATEGORIES.map((c) => (
              <label key={c} className="flex items-center gap-1 text-sm">
                <input type="checkbox" className="size-4 accent-primary" checked={categories.includes(c)} onChange={() => toggle(c)} />{categoryLabel(c)}
              </label>
            ))}
          </div>
        </div>
        <Button disabled={!name.trim() || categories.length === 0 || save.isPending} onClick={() => save.mutate()}>{profile ? 'Save profile' : 'Create profile'}</Button>
        {profile && (
          <div className="space-y-2 border-t pt-3">
            <Label>Credentials (expired ones block new work)</Label>
            {profile.credentials.map((c) => (
              <div key={c.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
                <span>
                  <span className="font-medium">{c.label}</span><span className="ml-2 text-muted-foreground">{c.kind}{c.jurisdiction ? ` · ${c.jurisdiction}` : ''}</span>
                  <span className={`ml-2 text-xs ${c.expired ? 'text-destructive' : 'text-muted-foreground'}`}>{c.expired ? 'expired' : 'until'} {fmtDate(c.expiresAt)}</span>
                </span>
                <ConfirmButton size="sm" variant="ghost" onConfirm={() => removeCred.mutate(c.id)}>Remove</ConfirmButton>
              </div>
            ))}
            <div className="flex flex-wrap items-end gap-2">
              <Select className="w-36" value={credKind} onChange={(e) => setCredKind(e.target.value)}>
                <option>License</option><option>Insurance</option><option>Certification</option>
              </Select>
              <Input className="w-56" placeholder="Label" value={credLabel} onChange={(e) => setCredLabel(e.target.value)} />
              <Input className="w-40" type="date" value={credExpires} onChange={(e) => setCredExpires(e.target.value)} />
              <Button size="sm" disabled={!credLabel.trim() || !credExpires || addCred.isPending} onClick={() => addCred.mutate()}>Add</Button>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

/** The seller's view of one request: accept/decline, start, check in/out with position, complete, message. */
export function VendorRequestPage() {
  const { requestId } = useParams({ strict: false }) as { requestId: string };
  const key = ['vendor', 'request', requestId];
  const { data: r } = useQuery({ queryKey: key, queryFn: () => api.get<RequestDetail>(`/api/vendor/requests/${requestId}`) });
  const [reason, setReason] = useState('');
  const [message, setMessage] = useState('');
  const act = (path: string, success: string) =>
    useApiMutation({ mutationFn: (body?: unknown) => api.post(`/api/vendor/requests/${requestId}${path}`, body), invalidate: [['vendor']], success });
  const accept = act('/accept', 'Accepted');
  const decline = act('/decline', 'Declined');
  const start = act('/start', 'Started');
  const complete = act('/complete', 'Marked complete');
  const position = useApiMutation({
    mutationFn: (kind: 'check-in' | 'check-out') =>
      new Promise<{ latitude: number; longitude: number }>((resolve, reject) =>
        navigator.geolocation.getCurrentPosition((p) => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude }), reject),
      ).then((pos) => api.post(`/api/vendor/requests/${requestId}/${kind}`, pos)),
    invalidate: [key], success: 'Position recorded', errorFallback: 'Could not read your position',
  });
  const send = useApiMutation({
    mutationFn: () => api.post(`/api/vendor/requests/${requestId}/messages`, { body: message.trim() }),
    invalidate: [key], onSuccess: () => setMessage(''),
  });
  if (!r) return <p className="text-sm text-muted-foreground">Loading…</p>;
  const working = r.status === 'Accepted' || r.status === 'InProgress';
  return (
    <div className="max-w-4xl space-y-6">
      <Link to="/vendor" className="text-sm text-muted-foreground hover:underline">← Vendor portal</Link>
      <RequestFacts r={r} side="vendor" />
      <Card>
        <CardHeader><CardTitle className="text-base">Actions</CardTitle></CardHeader>
        <CardContent className="flex flex-wrap items-end gap-3">
          {r.status === 'Submitted' && (
            <>
              <Button onClick={() => accept.mutate(undefined)}>Accept</Button>
              <Input className="w-56" placeholder="Decline reason" value={reason} onChange={(e) => setReason(e.target.value)} />
              <Button variant="outline" disabled={!reason.trim()} onClick={() => decline.mutate({ reason })}>Decline</Button>
            </>
          )}
          {r.status === 'Accepted' && <Button onClick={() => start.mutate(undefined)}>Start work</Button>}
          {working && (
            <>
              <Button variant="outline" onClick={() => position.mutate('check-in')}>Check in (GPS)</Button>
              <Button variant="outline" onClick={() => position.mutate('check-out')}>Check out (GPS)</Button>
            </>
          )}
          {r.status === 'InProgress' && (
            <>
              <Input className="w-64" placeholder="Completion summary" value={reason} onChange={(e) => setReason(e.target.value)} />
              <Button onClick={() => complete.mutate({ summary: reason })}>Mark complete</Button>
            </>
          )}
        </CardContent>
      </Card>
      <Timeline r={r} />
      {!['Declined', 'Cancelled', 'Verified'].includes(r.status) && (
        <div className="flex gap-2">
          <Textarea rows={2} value={message} placeholder="Message the requester" onChange={(e) => setMessage(e.target.value)} />
          <Button disabled={!message.trim() || send.isPending} onClick={() => send.mutate()}>Send</Button>
        </div>
      )}
    </div>
  );
}
