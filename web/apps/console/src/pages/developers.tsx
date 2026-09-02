import { api } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog,
  Input, Label, Select, Table, TableBody, TableCell, TableHead, TableHeader,
  TableRow } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';

type Role = { id: string; name: string };
type ApiKeyRow = {
  id: string; name: string; prefix: string; role: string;
  scopePath: string | null; createdAt: string; lastUsedAt: string | null;
  expiresAt: string | null; revoked: boolean;
};
type Webhook = {
  id: string; url: string; events: string[]; active: boolean; createdAt: string;
  lastDelivery: { eventName: string; ok: boolean; statusCode: number | null; occurredAt: string } | null;
};

/** The integration surface (ADR 40): server-to-server keys and outbound webhooks. */
export function DevelopersPage() {
  return (
    <div className="max-w-4xl space-y-6">
      <h1 className="text-2xl font-semibold">Developers</h1>
      <ApiKeysCard />
      <WebhooksCard />
    </div>
  );
}

function SecretReveal({ secret, note }: { secret: string; note: string }) {
  return (
    <div className="space-y-2 rounded-md border border-warning/40 bg-warning/10 p-3">
      <p className="text-sm font-medium">Copy this now - it will not be shown again.</p>
      <code className="block break-all rounded bg-background p-2 text-xs">{secret}</code>
      <p className="text-xs text-muted-foreground">{note}</p>
    </div>
  );
}

function ApiKeysCard() {
  const { data: keys } = useQuery({
    queryKey: ['api-keys'],
    queryFn: () => api.get<ApiKeyRow[]>('/api/api-keys'),
  });
  const { data: roles } = useQuery({
    queryKey: ['roles'],
    queryFn: () => api.get<Role[]>('/api/roles'),
  });
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [roleId, setRoleId] = useState('');
  const [secret, setSecret] = useState<string | null>(null);

  const create = useApiMutation({
    mutationFn: () =>
      api.post<{ id: string; secret: string }>('/api/api-keys', { name: name.trim(), roleId }),
    invalidate: [['api-keys']],
    onSuccess: ({ secret: revealed }) => setSecret(revealed),
  });
  const revoke = useApiMutation({
    mutationFn: (id: string) => api.del(`/api/api-keys/${id}`),
    invalidate: [['api-keys']],
    success: 'Key revoked',
  });
  const rotate = useApiMutation({
    mutationFn: (id: string) =>
      api.post<{ id: string; secret: string }>(`/api/api-keys/${id}/rotate`, {}),
    invalidate: [['api-keys']],
    onSuccess: ({ secret: revealed }) => {
      setSecret(revealed);
      setOpen(true); // the reveal lives in the dialog - open it to show the new secret
    },
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between">
          API keys
          <FormDialog
            open={open}
            onOpenChange={(next) => {
              setOpen(next);
              if (!next) {
                setSecret(null);
                setName('');
              }
            }}
            trigger={<Button size="sm">New key</Button>}
            title="New API key"
            description="The key acts as a service principal holding the role you pick - grant it as little as possible."
          >
            {secret ? (
              <SecretReveal
                secret={secret}
                note='Send it as "Authorization: Bearer <key>".'
              />
            ) : (
              <div className="space-y-3">
                <div className="space-y-1">
                  <Label htmlFor="key-name">Name</Label>
                  <Input id="key-name" value={name}
                    onChange={(e) => setName(e.target.value)} placeholder="ci-deploy" />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="key-role">Role</Label>
                  <Select id="key-role" value={roleId}
                    onChange={(e) => setRoleId(e.target.value)}>
                    <option value="">Choose…</option>
                    {roles?.map((r) => (
                      <option key={r.id} value={r.id}>{r.name}</option>
                    ))}
                  </Select>
                </div>
                <Button className="w-full" disabled={!name.trim() || !roleId || create.isPending}
                  onClick={() => create.mutate()}>
                  Create key
                </Button>
              </div>
            )}
          </FormDialog>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <p className="mb-3 text-sm text-muted-foreground">
          Authenticate with <code className="rounded bg-muted px-1">Authorization: Bearer locintel_…</code>{' '}
          against this console's origin. The full contract:{' '}
          <a href="/openapi/v1.json" target="_blank" rel="noreferrer" className="underline">
            OpenAPI spec
          </a>
          .
        </p>
        {keys && keys.length > 0 ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Key</TableHead>
                <TableHead>Role</TableHead>
                <TableHead>Last used</TableHead>
                <TableHead>Expires</TableHead>
                <TableHead className="w-24"><span className="sr-only">Actions</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {keys.map((k) => (
                <TableRow key={k.id} className={k.revoked ? 'opacity-50' : ''}>
                  <TableCell className="font-medium">{k.name}</TableCell>
                  <TableCell className="font-mono text-xs">{k.prefix}…</TableCell>
                  <TableCell className="text-muted-foreground">{k.role}</TableCell>
                  <TableCell className="text-muted-foreground">
                    {k.revoked ? 'Revoked' : k.lastUsedAt ? fmtDateTime(k.lastUsedAt) : 'never'}
                  </TableCell>
                  <TableCell
                    className={
                      k.expiresAt && new Date(k.expiresAt).getTime() - Date.now() < 7 * 86_400_000
                        ? 'text-warning-foreground'
                        : 'text-muted-foreground'
                    }
                  >
                    {k.expiresAt
                      ? new Date(k.expiresAt).getTime() < Date.now()
                        ? 'Expired'
                        : fmtDateTime(k.expiresAt)
                      : '—'}
                  </TableCell>
                  <TableCell className="space-x-1 text-right">
                    {!k.revoked && (
                      <>
                        <ConfirmButton variant="ghost" size="sm" disabled={rotate.isPending}
                          confirmLabel="Rotate? Old key gets 24h"
                          onConfirm={() => rotate.mutate(k.id)}>
                          Rotate
                        </ConfirmButton>
                        <ConfirmButton size="sm" disabled={revoke.isPending}
                          onConfirm={() => revoke.mutate(k.id)}>
                          Revoke
                        </ConfirmButton>
                      </>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p className="text-sm text-muted-foreground">
            No API keys yet. Create one for server-to-server access.
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function WebhooksCard() {
  const { data: hooks } = useQuery({
    queryKey: ['webhooks'],
    queryFn: () => api.get<Webhook[]>('/api/webhooks'),
  });
  const [open, setOpen] = useState(false);
  const [url, setUrl] = useState('');
  const [events, setEvents] = useState('');
  const [secret, setSecret] = useState<string | null>(null);

  const create = useApiMutation({
    mutationFn: () =>
      api.post<{ id: string; secret: string }>('/api/webhooks', {
        url: url.trim(),
        events: events
          .split(',')
          .map((e) => e.trim())
          .filter(Boolean),
      }),
    invalidate: [['webhooks']],
    onSuccess: ({ secret: revealed }) => setSecret(revealed),
  });
  const ping = useApiMutation({
    mutationFn: (id: string) => api.post(`/api/webhooks/${id}/ping`),
    invalidate: [['webhooks']],
    success: 'Ping queued - check your endpoint',
  });
  const remove = useApiMutation({
    mutationFn: (id: string) => api.del(`/api/webhooks/${id}`),
    invalidate: [['webhooks']],
    success: 'Webhook deleted',
  });
  const rotateSecret = useApiMutation({
    mutationFn: (id: string) => api.post<{ secret: string }>(`/api/webhooks/${id}/rotate-secret`),
    invalidate: [['webhooks']],
    onSuccess: ({ secret: revealed }) => {
      setSecret(revealed);
      setOpen(true); // the reveal lives in the dialog - open it to show the new secret
    },
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between">
          Webhooks
          <FormDialog
            open={open}
            onOpenChange={(next) => {
              setOpen(next);
              if (!next) {
                setSecret(null);
                setUrl('');
                setEvents('');
              }
            }}
            trigger={<Button size="sm">Add webhook</Button>}
            title="Add webhook"
            description="We POST signed JSON for each matching org event. Verify with the X-LocIntel-Signature header."
          >
            {secret ? (
              <SecretReveal
                secret={secret}
                note="Verify deliveries: v1 = HMAC-SHA256(secret, '{t}.{body}')."
              />
            ) : (
              <div className="space-y-3">
                <div className="space-y-1">
                  <Label htmlFor="hook-url">URL</Label>
                  <Input id="hook-url" value={url}
                    onChange={(e) => setUrl(e.target.value)}
                    placeholder="https://example.com/locintel-hooks" />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="hook-events">
                    Events (comma-separated, blank = all; wildcards like site.*)
                  </Label>
                  <Input id="hook-events" value={events}
                    onChange={(e) => setEvents(e.target.value)}
                    placeholder="site.*, org.renamed" />
                </div>
                <Button className="w-full" disabled={!url.trim() || create.isPending}
                  onClick={() => create.mutate()}>
                  Add webhook
                </Button>
              </div>
            )}
          </FormDialog>
        </CardTitle>
      </CardHeader>
      <CardContent>
        {hooks && hooks.length > 0 ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>URL</TableHead>
                <TableHead>Events</TableHead>
                <TableHead>Last delivery</TableHead>
                <TableHead className="w-40"><span className="sr-only">Actions</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {hooks.map((h) => (
                <TableRow key={h.id}>
                  <TableCell className="max-w-56 truncate font-mono text-xs">{h.url}</TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {h.events.length === 0 ? 'all' : h.events.join(', ')}
                  </TableCell>
                  <TableCell className="text-xs">
                    {h.lastDelivery ? (
                      <span className={h.lastDelivery.ok ? 'text-success-foreground' : 'text-destructive'}>
                        {h.lastDelivery.ok ? '✓' : '✗'} {h.lastDelivery.eventName} ·{' '}
                        {fmtDateTime(h.lastDelivery.occurredAt)}
                      </span>
                    ) : (
                      <span className="text-muted-foreground">none yet</span>
                    )}
                  </TableCell>
                  <TableCell className="space-x-1 text-right">
                    <Button variant="ghost" size="sm" disabled={ping.isPending}
                      onClick={() => ping.mutate(h.id)}>
                      Ping
                    </Button>
                    <ConfirmButton variant="ghost" size="sm" disabled={rotateSecret.isPending}
                      confirmLabel="Rotate? Old secret signs 24h"
                      onConfirm={() => rotateSecret.mutate(h.id)}>
                      Rotate secret
                    </ConfirmButton>
                    <ConfirmButton size="sm" disabled={remove.isPending}
                      onConfirm={() => remove.mutate(h.id)}>
                      Delete
                    </ConfirmButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p className="text-sm text-muted-foreground">
            No webhooks yet. Add one to push org events to your systems.
          </p>
        )}
      </CardContent>
    </Card>
  );
}
