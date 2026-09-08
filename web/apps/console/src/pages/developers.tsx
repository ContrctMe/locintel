import { api } from '@locintel/api';
import { Alert, AlertAction, AlertDescription, AlertTitle, Button, CodeBlock, ConfirmButton, Field, FieldLabel, FormDialog, Input, Select, type ColumnDef, type DataGridFeatures } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { KeyRound } from 'lucide-react';
import { useMemo, useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { Grid, PageHeader } from '../components/page';
import { useApiMutation } from '../lib/mutation';

/** The integration surface (ADR 40): server-to-server keys and outbound webhooks. */
export function DevelopersPage() {
  return (
    <div className="max-w-4xl space-y-6">
      <PageHeader title="Developers" description="Server-to-server keys and outbound webhooks (ADR 40)." />
      <ApiKeysCard />
      <WebhooksCard />
    </div>
  );
}

function SecretReveal({ secret, note }: { secret: string; note: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Alert variant="warning">
      <KeyRound aria-hidden />
      <AlertTitle>Copy this now - it will not be shown again.</AlertTitle>
      <AlertDescription>
        <code className="block break-all rounded bg-background/60 p-2 text-xs">{secret}</code>
        <span>{note}</span>
      </AlertDescription>
      <AlertAction>
        <Button
          size="xs"
          variant="outline"
          onClick={async () => {
            await navigator.clipboard.writeText(secret);
            setCopied(true);
            setTimeout(() => setCopied(false), 1500);
          }}
        >
          {copied ? 'Copied' : 'Copy'}
        </Button>
      </AlertAction>
    </Alert>
  );
}

function ApiKeysCard() {
  const { data: keys } = useQuery({
    queryKey: ['api-keys'],
    queryFn: ({ signal }) => api.get('/api/api-keys', { signal }),
  });
  const { data: roles } = useQuery({
    queryKey: ['roles'],
    queryFn: ({ signal }) => api.get('/api/roles', { signal }),
  });
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [roleId, setRoleId] = useState('');
  const [secret, setSecret] = useState<string | null>(null);

  const create = useApiMutation({
    mutationFn: () => api.post('/api/api-keys', { name: name.trim(), roleId }),
    invalidate: [['api-keys']],
    onSuccess: ({ secret: revealed }) => setSecret(revealed),
  });
  const revoke = useApiMutation({
    mutationFn: (id: string) => api.del('/api/api-keys/{id}', { path: { id } }),
    invalidate: [['api-keys']],
    success: 'Key revoked',
  });
  const rotate = useApiMutation({
    mutationFn: (id: string) =>
      api.post('/api/api-keys/{id}/rotate', {}, { path: { id } }),
    invalidate: [['api-keys']],
    onSuccess: ({ secret: revealed }) => {
      setSecret(revealed);
      setOpen(true); // the reveal lives in the dialog - open it to show the new secret
    },
  });
  type KeyRow = NonNullable<typeof keys>[number];
  const keyColumns = useMemo<ColumnDef<DataGridFeatures, KeyRow>[]>(
    () => [
      { id: 'name', accessorKey: 'name', header: 'Name', cell: ({ row }) => <span className={row.original.revoked ? 'font-medium opacity-50' : 'font-medium'}>{row.original.name}</span> },
      { id: 'prefix', header: 'Key', cell: ({ row }) => <span className="font-mono text-xs">{row.original.prefix}…</span> },
      { id: 'role', accessorKey: 'role', header: 'Role', cell: ({ row }) => <span className="text-muted-foreground">{row.original.role}</span> },
      {
        id: 'lastUsed',
        header: 'Last used',
        cell: ({ row }) => (
          <span className="text-muted-foreground">
            {row.original.revoked ? 'Revoked' : row.original.lastUsedAt ? fmtDateTime(row.original.lastUsedAt) : 'never'}
          </span>
        ),
      },
      {
        id: 'expires',
        header: 'Expires',
        cell: ({ row }) => {
          const k = row.original;
          const soon = k.expiresAt && new Date(k.expiresAt).getTime() - Date.now() < 7 * 86_400_000;
          return (
            <span className={soon ? 'text-warning-foreground' : 'text-muted-foreground'}>
              {k.expiresAt ? (new Date(k.expiresAt).getTime() < Date.now() ? 'Expired' : fmtDateTime(k.expiresAt)) : '—'}
            </span>
          );
        },
      },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) =>
          row.original.revoked ? null : (
            <div className="space-x-1 text-right">
              <ConfirmButton variant="ghost" size="sm" disabled={rotate.isPending} confirmLabel="Rotate this key?" description="The old key keeps working for 24 hours." onConfirm={() => rotate.mutate(row.original.id)}>
                Rotate
              </ConfirmButton>
              <ConfirmButton size="sm" disabled={revoke.isPending} confirmLabel="Revoke this key?" description="Anything using it stops at once." onConfirm={() => revoke.mutate(row.original.id)}>
                Revoke
              </ConfirmButton>
            </div>
          ),
        meta: { headerClassName: 'w-40' },
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  return (
    <Grid
      title="API keys"
      children={
        <div className="border-t px-4 py-4">
          <CodeBlock
            code={`curl -H "Authorization: Bearer locintel_…" \\\n  "${window.location.origin}/api/sites?limit=5"`}
            language="bash"
            highlight={false}
          />
        </div>
      }
      description={
        <>
          Authenticate with <code className="rounded bg-muted px-1">Authorization: Bearer locintel_…</code>{' '}
          against this console's origin. The full contract:{' '}
          <a href="/openapi/v1.json" target="_blank" rel="noreferrer" className="underline">
            OpenAPI spec
          </a>
          .
        </>
      }
      actions={
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
                <Field>
                  <FieldLabel htmlFor="key-name">Name</FieldLabel>
                  <Input id="key-name" value={name}
                    onChange={(e) => setName(e.target.value)} placeholder="ci-deploy" />
                </Field>
                <Field>
                  <FieldLabel htmlFor="key-role">Role</FieldLabel>
                  <Select id="key-role" value={roleId}
                    onChange={(e) => setRoleId(e.target.value)}>
                    <option value="">Choose…</option>
                    {roles?.map((r) => (
                      <option key={r.id} value={r.id}>{r.name}</option>
                    ))}
                  </Select>
                </Field>
                <Button className="w-full" disabled={!name.trim() || !roleId || create.isPending}
                  onClick={() => create.mutate()}>
                  Create key
                </Button>
              </div>
            )}
          </FormDialog>
      }
      columns={keyColumns}
      rows={keys ?? []}
      getRowId={(k) => k.id}
      isLoading={keys === undefined}
      emptyMessage="No API keys yet. Create one for server-to-server access."
    />
  );
}

function WebhooksCard() {
  const { data: hooks } = useQuery({
    queryKey: ['webhooks'],
    queryFn: ({ signal }) => api.get('/api/webhooks', { signal }),
  });
  const [open, setOpen] = useState(false);
  const [url, setUrl] = useState('');
  const [events, setEvents] = useState('');
  const [secret, setSecret] = useState<string | null>(null);

  const create = useApiMutation({
    mutationFn: () =>
      api.post('/api/webhooks', {
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
    mutationFn: (id: string) => api.post('/api/webhooks/{id}/ping', undefined, { path: { id } }),
    invalidate: [['webhooks']],
    success: 'Ping queued - check your endpoint',
  });
  const remove = useApiMutation({
    mutationFn: (id: string) => api.del('/api/webhooks/{id}', { path: { id } }),
    invalidate: [['webhooks']],
    success: 'Webhook deleted',
  });
  const rotateSecret = useApiMutation({
    mutationFn: (id: string) => api.post('/api/webhooks/{id}/rotate-secret', undefined, { path: { id } }),
    invalidate: [['webhooks']],
    onSuccess: ({ secret: revealed }) => {
      setSecret(revealed);
      setOpen(true); // the reveal lives in the dialog - open it to show the new secret
    },
  });
  type HookRow = NonNullable<typeof hooks>[number];
  const hookColumns = useMemo<ColumnDef<DataGridFeatures, HookRow>[]>(
    () => [
      { id: 'url', accessorKey: 'url', header: 'URL', cell: ({ row }) => <span className="block max-w-56 truncate font-mono text-xs">{row.original.url}</span> },
      {
        id: 'events',
        header: 'Events',
        cell: ({ row }) => <span className="text-xs text-muted-foreground">{row.original.events.length === 0 ? 'all' : row.original.events.join(', ')}</span>,
      },
      {
        id: 'delivery',
        header: 'Last delivery',
        cell: ({ row }) => {
          const d = row.original.lastDelivery;
          return d ? (
            <span className={`text-xs ${d.ok ? 'text-success-foreground' : 'text-destructive'}`}>
              {d.ok ? '✓' : '✗'} {d.eventName} · {fmtDateTime(d.occurredAt)}
            </span>
          ) : (
            <span className="text-xs text-muted-foreground">none yet</span>
          );
        },
      },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) => (
          <div className="space-x-1 text-right">
            <Button variant="ghost" size="sm" disabled={ping.isPending} onClick={() => ping.mutate(row.original.id)}>
              Ping
            </Button>
            <ConfirmButton variant="ghost" size="sm" disabled={rotateSecret.isPending} confirmLabel="Rotate the secret?" description="The old secret keeps signing for 24 hours." onConfirm={() => rotateSecret.mutate(row.original.id)}>
              Rotate secret
            </ConfirmButton>
            <ConfirmButton size="sm" disabled={remove.isPending} confirmLabel="Delete this webhook?" onConfirm={() => remove.mutate(row.original.id)}>
              Delete
            </ConfirmButton>
          </div>
        ),
        meta: { headerClassName: 'w-64' },
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  return (
    <Grid
      title="Webhooks"
      actions={
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
                <Field>
                  <FieldLabel htmlFor="hook-url">URL</FieldLabel>
                  <Input id="hook-url" value={url}
                    onChange={(e) => setUrl(e.target.value)}
                    placeholder="https://example.com/locintel-hooks" />
                </Field>
                <Field>
                  <FieldLabel htmlFor="hook-events">
                    Events (comma-separated, blank = all; wildcards like site.*)
                  </FieldLabel>
                  <Input id="hook-events" value={events}
                    onChange={(e) => setEvents(e.target.value)}
                    placeholder="site.*, org.renamed" />
                </Field>
                <Button className="w-full" disabled={!url.trim() || create.isPending}
                  onClick={() => create.mutate()}>
                  Add webhook
                </Button>
              </div>
            )}
          </FormDialog>
      }
      columns={hookColumns}
      rows={hooks ?? []}
      getRowId={(h) => h.id}
      isLoading={hooks === undefined}
      emptyMessage="No webhooks yet. Add one to push org events to your systems."
    />
  );
}
