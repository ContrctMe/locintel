import { api } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, Input, Label, Select } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { fmtDate } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import { can, useMe } from '../session';

type SsoStatus = { available: boolean; entitled: boolean };
type AttributeDefinition = {
  id: string; key: string; label: string; type: string; public: boolean;
};

type Billing = {
  provider: string;
  planId: string | null;
  planName: string;
  status: string | null;
  currentPeriodEnd: string | null;
  portalAvailable: boolean;
  plans: { id: string; name: string; monthlyPriceUsd: number }[];
};

export function SettingsPage() {
  const { data: me } = useMe();
  const activeOrg =
    me?.tier === 'user' ? me.organizations.find((o) => o.id === me.activeOrg) : undefined;
  const [name, setName] = useState<string | null>(null);

  const rename = useApiMutation({
    mutationFn: (value: string) => api.put('/api/org', { name: value }),
    invalidate: [['me']],
    success: 'Organization renamed',
    onSuccess: () => setName(null),
  });
  const exportData = useApiMutation({
    mutationFn: () => api.post('/api/org/export'),
    success: 'Export queued - check Files shortly',
  });
  const { data: billing } = useQuery({
    queryKey: ['billing'],
    queryFn: () => api.get<Billing>('/api/billing'),
  });
  const checkout = useApiMutation({
    mutationFn: (planId: string) =>
      api.post<{ url: string }>('/api/billing/checkout', { planId, returnPath: '/settings' }),
    onSuccess: ({ url }) => {
      location.href = url;
    },
  });
  const portal = useApiMutation({
    mutationFn: () => api.post<{ url: string }>('/api/billing/portal', { returnPath: '/settings' }),
    onSuccess: ({ url }) => {
      location.href = url;
    },
  });
  const { data: sso } = useQuery({
    queryKey: ['sso'],
    queryFn: () => api.get<SsoStatus>('/api/org/sso'),
  });
  const { data: publicUrl } = useQuery({
    queryKey: ['public-url'],
    queryFn: () => api.get<{ url: string; embedSnippet: string }>('/api/org/public-url'),
  });
  const { data: closure } = useQuery({
    queryKey: ['closure'],
    queryFn: () =>
      api.get<{ requestedAt: string | null; purgesAt: string | null }>('/api/org/closure'),
  });
  const requestClose = useApiMutation({
    mutationFn: () => api.post('/api/org/close'),
    invalidate: [['closure']],
    success: 'Closure scheduled - every manager has been notified',
  });
  const cancelClose = useApiMutation({
    mutationFn: () => api.post('/api/org/close/cancel'),
    invalidate: [['closure']],
    success: 'Closure canceled',
  });
  const ssoPortal = useApiMutation({
    mutationFn: (intent: 'sso' | 'dsync') =>
      api.post<{ url: string }>('/api/org/sso/portal', { intent, returnPath: '/settings' }),
    onSuccess: ({ url }) => {
      location.href = url;
    },
  });

  if (!activeOrg) return null;
  const draft = name ?? activeOrg.name;
  return (
    <div className="max-w-lg space-y-6">
      <h1 className="text-2xl font-semibold">Organization settings</h1>
      <Card>
        <CardHeader><CardTitle>Profile</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          <div className="space-y-1">
            <Label htmlFor="org-rename">Name</Label>
            <Input id="org-rename" value={draft} onChange={(e) => setName(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="org-slug">URL slug</Label>
            <Input id="org-slug" value={activeOrg.slug} disabled />
          </div>
          <Button
            disabled={draft === activeOrg.name || !draft.trim() || rename.isPending}
            onClick={() => rename.mutate(draft.trim())}
          >
            Save
          </Button>
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle>Billing</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          {billing?.status === 'PastDue' && (
            <div className="rounded-md bg-warning/15 px-3 py-2 text-sm text-warning-foreground">
              <span className="font-semibold">Payment failed.</span> Your features continue
              while the charge retries
              {billing.currentPeriodEnd && ` until ${fmtDate(billing.currentPeriodEnd)}`} — update
              your card via Manage billing below.
            </div>
          )}
          {billing && (
            <>
              <div className="text-sm">
                <span className="font-medium">{billing.planName} plan</span>
                {billing.status && (
                  <span className="ml-2 text-muted-foreground">
                    {billing.status}
                    {billing.currentPeriodEnd &&
                      ` · renews ${fmtDate(billing.currentPeriodEnd)}`}
                  </span>
                )}
              </div>
              <div className="flex flex-wrap gap-2">
                {billing.plans
                  .filter((p) => p.id !== billing.planId || billing.status === 'Canceled')
                  .map((p) => (
                    <Button
                      key={p.id}
                      variant="outline"
                      size="sm"
                      disabled={checkout.isPending}
                      onClick={() => checkout.mutate(p.id)}
                    >
                      {billing.planId && billing.status !== 'Canceled'
                        ? `Switch to ${p.name}`
                        : `Upgrade to ${p.name}`}{' '}
                      · ${p.monthlyPriceUsd}/mo
                    </Button>
                  ))}
                {billing.portalAvailable && (
                  <Button variant="ghost" size="sm" disabled={portal.isPending}
                    onClick={() => portal.mutate()}>
                    Manage billing
                  </Button>
                )}
              </div>
              <p className="text-xs text-muted-foreground">
                Checkout and billing management are hosted by your payment provider
                ({billing.provider}). Plan changes apply automatically.
              </p>
            </>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Single sign-on</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          {!sso ? null : !sso.available ? (
            <p className="text-sm text-muted-foreground">
              Enterprise SSO and directory sync are not supported by this
              installation&apos;s auth provider.
            </p>
          ) : !sso.entitled ? (
            <p className="text-sm text-muted-foreground">
              Connect your identity provider and sync your employee directory
              automatically. Available on the Scale plan - upgrade under Billing above.
            </p>
          ) : (
            <>
              <p className="text-sm text-muted-foreground">
                Configuration is hosted by the auth provider: connect your identity
                provider, or sync your employee directory so joiners and leavers are
                provisioned automatically.
              </p>
              <div className="flex flex-wrap gap-2">
                <Button variant="outline" size="sm" disabled={ssoPortal.isPending}
                  onClick={() => ssoPortal.mutate('sso')}>
                  Configure SSO
                </Button>
                <Button variant="outline" size="sm" disabled={ssoPortal.isPending}
                  onClick={() => ssoPortal.mutate('dsync')}>
                  Configure directory sync
                </Button>
              </div>
            </>
          )}
        </CardContent>
      </Card>

      {can(me, 'sites:manage') && <SiteAttributesCard />}

      <Card>
        <CardHeader><CardTitle>Public locator</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          {publicUrl && (
            <>
              <p className="text-sm text-muted-foreground">
                Your locations live at{' '}
                <a href={publicUrl.url} target="_blank" rel="noreferrer"
                  className="text-foreground underline underline-offset-4">
                  {publicUrl.url}
                </a>
                . Embed the locator on your own website with this snippet:
              </p>
              <code tabIndex={0} className="block overflow-x-auto whitespace-pre rounded-md bg-muted p-3 text-xs">
                {publicUrl.embedSnippet}
              </code>
            </>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Your data</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          <p className="text-sm text-muted-foreground">
            Take a full archive of this organization&apos;s data - sites, people, roles,
            entitlements, and audit history. The archive is delivered to Files.
          </p>
          <Button
            variant="outline"
            disabled={exportData.isPending}
            onClick={() => exportData.mutate()}
          >
            {exportData.isSuccess ? 'Queued - check Files shortly' : 'Export org data'}
          </Button>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Close this organization</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          {closure?.requestedAt ? (
            <>
              <p className="rounded-md bg-destructive/10 px-3 py-2 text-sm text-destructive">
                Closure scheduled: all data is permanently deleted on{' '}
                <span className="font-semibold">{fmtDate(closure.purgesAt!)}</span>. Everything
                keeps working until then.
              </p>
              <Button variant="outline" disabled={cancelClose.isPending}
                onClick={() => cancelClose.mutate()}>
                Cancel closure
              </Button>
            </>
          ) : (
            <>
              <p className="text-sm text-muted-foreground">
                Schedules permanent deletion of this organization and all its data after a
                30-day grace window. Every manager is notified, everything keeps working
                until the deadline, and any manager can cancel. Export your data first.
              </p>
              <ConfirmButton variant="destructive" confirmLabel="Schedule permanent deletion?"
                disabled={requestClose.isPending}
                onConfirm={() => requestClose.mutate()}>
                Close organization
              </ConfirmButton>
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function SiteAttributesCard() {
  const [key, setKey] = useState('');
  const [label, setLabel] = useState('');
  const [type, setType] = useState('Text');
  const [isPublic, setIsPublic] = useState(false);

  const { data: definitions } = useQuery({
    queryKey: ['site-attributes'],
    queryFn: () => api.get<AttributeDefinition[]>('/api/sites/attributes'),
  });
  const create = useApiMutation({
    mutationFn: () =>
      api.post('/api/sites/attributes', { key: key.trim(), label: label.trim(), type, public: isPublic }),
    invalidate: [['site-attributes']],
    success: 'Attribute added',
    onSuccess: () => {
      setKey('');
      setLabel('');
      setIsPublic(false);
    },
  });
  const remove = useApiMutation({
    mutationFn: (id: string) => api.del(`/api/sites/attributes/${id}`),
    invalidate: [['site-attributes'], ['site']],
    success: 'Attribute removed - its values are gone from every site',
  });

  return (
    <Card>
      <CardHeader><CardTitle>Site attributes</CardTitle></CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm text-muted-foreground">
          Your own fields on every site - a drive-thru flag, a cost center, a manager name.
          Public attributes appear on the site&apos;s public page; the rest stay internal.
        </p>
        {definitions?.map((d) => (
          <div key={d.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
            <span>
              <span className="font-medium">{d.label}</span>
              <span className="ml-2 text-muted-foreground">
                {d.key} · {d.type}
                {d.public && ' · public'}
              </span>
            </span>
            <ConfirmButton size="sm" variant="ghost" confirmLabel="Delete? Values go too"
              disabled={remove.isPending} onConfirm={() => remove.mutate(d.id)}>
              Delete
            </ConfirmButton>
          </div>
        ))}
        <div className="flex flex-wrap items-end gap-2">
          <div className="space-y-1">
            <Label htmlFor="attr-label">Label</Label>
            <Input id="attr-label" className="w-40" value={label} placeholder="Drive-thru"
              onChange={(e) => {
                setLabel(e.target.value);
                setKey(e.target.value.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, ''));
              }} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="attr-key">Key</Label>
            <Input id="attr-key" className="w-36 font-mono text-xs" value={key}
              onChange={(e) => setKey(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="attr-type">Type</Label>
            <Select id="attr-type" className="w-28" value={type}
              onChange={(e) => setType(e.target.value)}>
              <option>Text</option>
              <option>Number</option>
              <option>Boolean</option>
            </Select>
          </div>
          <label className="flex h-9 items-center gap-1.5 text-sm">
            <input type="checkbox" className="size-4 accent-primary" checked={isPublic}
              onChange={(e) => setIsPublic(e.target.checked)} />
            Public
          </label>
          <Button size="sm" disabled={!key.trim() || !label.trim() || create.isPending}
            onClick={() => create.mutate()}>
            Add
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
