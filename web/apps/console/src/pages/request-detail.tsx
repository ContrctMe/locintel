import { api, type components } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Textarea } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import { RequestStatusBadge, categoryLabel } from './marketplace';

export type RequestDetail = components['schemas']['RequestDetail'];

/** Shared body for both sides of a request: facts, then the timeline. */
export function RequestFacts({ r, side }: { r: RequestDetail; side: 'requester' | 'vendor' }) {
  return (
    <>
      <div>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold">{r.title}</h1>
          <RequestStatusBadge status={r.status} />
          <span className="text-sm text-muted-foreground">{r.urgency}</span>
        </div>
        <p className="mt-1 text-sm text-muted-foreground">
          {categoryLabel(r.category)} · {side === 'requester' ? (r.vendorName ? `to ${r.vendorName}` : r.mode === 'Broadcast' ? 'open for quotes' : 'no vendor') : `from ${r.requesterName}`} · {r.siteName}
        </p>
      </div>
      <Card>
        <CardContent className="grid gap-x-8 gap-y-2 pt-4 text-sm sm:grid-cols-2">
          <Field label="Starts">{fmtDateTime(r.startsAt)}</Field>
          <Field label="Ends">{r.endsAt ? fmtDateTime(r.endsAt) : '—'}</Field>
          {r.rrule && <Field label="Recurs">{r.rrule} ({r.siteTimeZone})</Field>}
          {r.status === 'Submitted' && r.responseDueAt && (
            <Field label="Response due">{fmtDateTime(r.responseDueAt)}{Number(r.escalationCount) > 0 && ` · escalated ${r.escalationCount}×`}</Field>
          )}
          <Field label="Budget">{r.budgetAmount == null ? '—' : `${r.currency} ${Number(r.budgetAmount).toLocaleString()}`}</Field>
          {r.incidentId && <Field label="Incident"><Link to="/incidents/$incidentId" params={{ incidentId: r.incidentId }} className="hover:underline">Open</Link></Field>}
          {r.caseId && <Field label="Case"><Link to="/cases/$caseId" params={{ caseId: r.caseId }} className="hover:underline">Open</Link></Field>}
          {Object.keys(r.spec).length > 0 && (
            <div className="sm:col-span-2">
              <div className="text-xs uppercase text-muted-foreground">Specifics</div>
              <dl className="grid grid-cols-2 gap-x-4 gap-y-1 sm:grid-cols-3">
                {Object.entries(r.spec).map(([k, v]) => <div key={k}><dt className="text-xs text-muted-foreground">{k}</dt><dd>{v}</dd></div>)}
              </dl>
            </div>
          )}
          <div className="sm:col-span-2"><div className="text-xs uppercase text-muted-foreground">Details</div><p className="whitespace-pre-wrap">{r.details || '—'}</p></div>
          {r.declineReason && <Field label="Declined">{r.declineReason}</Field>}
          {r.completionSummary && <Field label="Completion">{r.completionSummary}</Field>}
          {r.disputeReason && <Field label="Dispute">{r.disputeReason}</Field>}
          {r.cancelReason && <Field label="Cancelled">{r.cancelReason}</Field>}
        </CardContent>
      </Card>
    </>
  );
}

export function Timeline({ r }: { r: RequestDetail }) {
  return (
    <Card>
      <CardHeader><CardTitle className="text-base">Timeline</CardTitle></CardHeader>
      <CardContent className="space-y-2 text-sm">
        {r.events.length === 0 && <p className="text-muted-foreground">Nothing yet.</p>}
        {r.events.map((e) => (
          <div key={e.id} className="flex gap-3">
            <span className="w-36 shrink-0 text-xs text-muted-foreground">{fmtDateTime(e.at)}</span>
            <span className="w-20 shrink-0 text-xs font-medium uppercase text-muted-foreground">{e.side}</span>
            <span>
              <span className="font-medium">{e.kind.replace(/([a-z])([A-Z])/g, '$1 $2')}</span>
              {e.body && <span className="ml-2">{e.body}</span>}
              {e.actor && <span className="ml-2 text-xs text-muted-foreground">{e.actor}</span>}
              {e.distanceFromSiteMeters != null && (
                <span className={`ml-2 text-xs ${e.withinGeofence ? 'text-emerald-700 dark:text-emerald-300' : 'text-destructive'}`}>
                  {Math.round(Number(e.distanceFromSiteMeters))} m from site
                </span>
              )}
            </span>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <div><div className="text-xs uppercase text-muted-foreground">{label}</div><div>{children}</div></div>;
}

/** The buyer's view: verify or dispute completed work, cancel, message the vendor. */
export function RequestDetailPage() {
  const { requestId } = useParams({ strict: false }) as { requestId: string };
  const key = ['marketplace', 'request', requestId];
  const { data: r } = useQuery({ queryKey: key, queryFn: ({ signal }) => api.get("/api/marketplace/requests/{id}", { signal, path: { id: requestId } }) });
  const [reason, setReason] = useState('');
  const [message, setMessage] = useState('');
  const submit = useApiMutation({ mutationFn: () => api.post('/api/marketplace/requests/{id}/submit', undefined, { path: { id: requestId } }), invalidate: [['marketplace']], success: 'Sent to the vendor' });
  const verify = useApiMutation({ mutationFn: () => api.post('/api/marketplace/requests/{id}/verify', undefined, { path: { id: requestId } }), invalidate: [['marketplace']], success: 'Work verified' });
  const dispute = useApiMutation({ mutationFn: (body: { reason: string }) => api.post('/api/marketplace/requests/{id}/dispute', body, { path: { id: requestId } }), invalidate: [['marketplace']], success: 'Dispute raised' });
  const cancel = useApiMutation({ mutationFn: (body: { reason: string }) => api.post('/api/marketplace/requests/{id}/cancel', body, { path: { id: requestId } }), invalidate: [['marketplace']], success: 'Request cancelled' });
  const award = useApiMutation({
    mutationFn: (quoteId: string) => api.post("/api/marketplace/requests/{id}/quotes/{quoteId}/accept", undefined, { path: { id: requestId, quoteId } }),
    invalidate: [['marketplace']], success: 'Quote accepted; the vendor is assigned',
  });
  const send = useApiMutation({
    mutationFn: () => api.post("/api/marketplace/requests/{id}/messages", { body: message.trim() }, { path: { id: requestId } }),
    invalidate: [key], onSuccess: () => setMessage(''),
  });
  if (!r) return <p className="text-sm text-muted-foreground">Loading…</p>;
  return (
    <div className="max-w-4xl space-y-6">
      <Link to="/marketplace" className="text-sm text-muted-foreground hover:underline">← Marketplace</Link>
      <RequestFacts r={r} side="requester" />
      {r.canManage && (
        <Card>
          <CardHeader><CardTitle className="text-base">Actions</CardTitle></CardHeader>
          <CardContent className="flex flex-wrap items-end gap-3">
            {r.status === 'Draft' && <Button onClick={() => submit.mutate(undefined)}>Send to vendor</Button>}
            {(r.status === 'Completed' || r.status === 'Disputed') && <Button onClick={() => verify.mutate(undefined)}>Verify work</Button>}
            {['Draft', 'Submitted', 'Accepted', 'Completed'].includes(r.status) && (
              <>
                <Input className="w-64" placeholder="Reason" value={reason} onChange={(e) => setReason(e.target.value)} />
                {r.status === 'Completed' && <Button variant="outline" disabled={!reason.trim()} onClick={() => dispute.mutate({ reason })}>Dispute</Button>}
                {r.status !== 'Completed' && <Button variant="destructive" disabled={!reason.trim()} onClick={() => cancel.mutate({ reason })}>Cancel</Button>}
              </>
            )}
          </CardContent>
        </Card>
      )}
      {r.mode === 'Broadcast' && (
        <Card>
          <CardHeader><CardTitle className="text-base">Quotes</CardTitle></CardHeader>
          <CardContent className="space-y-2 text-sm">
            <p className="text-muted-foreground">
              Sent to {r.recipients.length} vendor{r.recipients.length === 1 ? '' : 's'}: {r.recipients.map((x) => `${x.vendorName ?? x.vendorOrgId} (${x.status})`).join(', ') || 'none yet'}
            </p>
            {r.quotes.length === 0 && <p className="text-muted-foreground">No quotes yet.</p>}
            {r.quotes.map((q) => (
              <div key={q.id} className="flex items-center justify-between rounded-md border p-2">
                <span>
                  <span className="font-medium">{q.vendorName ?? q.vendorOrgId}</span> · {q.currency} {Number(q.amount).toLocaleString()}
                  {q.notes && <span className="ml-2 text-muted-foreground">{q.notes}</span>}
                  {q.validUntil && <span className="ml-2 text-xs text-muted-foreground">valid until {fmtDateTime(q.validUntil)}</span>}
                  <span className="ml-2 text-xs text-muted-foreground">{q.status}</span>
                </span>
                {r.canManage && r.status === 'Submitted' && q.status === 'Submitted' && (
                  <Button size="sm" onClick={() => award.mutate(q.id)}>Award</Button>
                )}
              </div>
            ))}
          </CardContent>
        </Card>
      )}
      <Timeline r={r} />
      {r.canManage && !['Declined', 'Cancelled', 'Verified'].includes(r.status) && (
        <div className="flex gap-2">
          <Textarea rows={2} value={message} placeholder="Message the vendor" onChange={(e) => setMessage(e.target.value)} />
          <Button disabled={!message.trim() || send.isPending} onClick={() => send.mutate()}>Send</Button>
        </div>
      )}
    </div>
  );
}
