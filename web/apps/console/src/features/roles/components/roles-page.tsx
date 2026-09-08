import { type components } from '@locintel/api';
import { Badge, Button, ConfirmButton, Field, FieldLabel, FormDialog, Input, Select, type ColumnDef, type DataGridFeatures } from '@locintel/ui';
import { useMemo, useState } from 'react';
import { fmtDate } from '../../../lib/format';
import { Grid, Loading, PageHeader } from '../../../components/page';
import { useApiMutation } from '../../../lib/mutation';
import { rolesApi } from '../api';
import { useGrantExceptions, useRoleHierarchy, useRoleMembers, useRoles } from '../hooks';
import { capabilityLabel } from '../labels';
import { GRANTABLE, grantKey, parseGrant } from '../schema';
import { RoleEditor } from './role-editor';

type Role = components['schemas']['RoleResponse'];
type Member = { userId: string; email: string; roles: string[] };
type Node = { id: string; name: string; depth: number; path: string };

/** The role editor: what a role grants, who holds it and where, and the exceptions. */
export function RolesPage() {
  const rolesQuery = useRoles();
  const membersQuery = useRoleMembers();
  const hierarchyQuery = useRoleHierarchy();
  const roles = rolesQuery.data;
  const members = membersQuery.data;
  const hierarchy = hierarchyQuery.data;

  const [editorOpen, setEditorOpen] = useState(false);
  const [editing, setEditing] = useState<Role | null>(null);

  const openCreate = () => {
    setEditing(null);
    setEditorOpen(true);
  };
  const openEdit = (role: Role) => {
    setEditing(role);
    setEditorOpen(true);
  };

  const remove = useApiMutation({
    mutationFn: rolesApi.remove,
    invalidate: [['roles']],
    success: 'Role deleted',
    errorFallback: 'Delete failed',
  });

  const roleColumns = useMemo<ColumnDef<DataGridFeatures, Role>[]>(
    () => [
      { id: 'name', accessorKey: 'name', header: 'Role', cell: ({ row }) => <span className="font-medium">{row.original.name}</span> },
      {
        id: 'grants',
        header: 'Grants',
        cell: ({ row }) => (
          <div className="flex max-w-md flex-wrap gap-1">
            {row.original.grants.map((g) => (
              <Badge key={grantKey(g)} variant="secondary" size="sm" title={grantKey(g)}>
                {capabilityLabel(grantKey(g))}
              </Badge>
            ))}
          </div>
        ),
      },
      {
        id: 'held',
        accessorKey: 'assignedCount',
        header: 'Held by',
        cell: ({ row }) => <span className="text-muted-foreground">{row.original.assignedCount}</span>,
        meta: { headerClassName: 'w-24' },
      },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) => (
          <div className="space-x-1 text-right">
            <Button variant="ghost" size="sm" onClick={() => openEdit(row.original)}>
              Edit
            </Button>
            <ConfirmButton
              size="sm"
              confirmLabel="Delete this role?"
              description="Members holding it lose those grants at once."
              disabled={remove.isPending}
              onConfirm={() => remove.mutate(row.original.id)}
            >
              Delete
            </ConfirmButton>
          </div>
        ),
        meta: { headerClassName: 'w-40' },
      },
    ],
    // openEdit and remove are stable for the page's life
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  if (rolesQuery.isPending || membersQuery.isPending || hierarchyQuery.isPending)
    return <Loading text="Loading roles…" />;
  if (rolesQuery.isError || membersQuery.isError)
    return <p className="text-sm text-destructive">Could not load roles.</p>;

  return (
    <div className="max-w-4xl space-y-6">
      <PageHeader
        title="Roles"
        description="What a role grants, who holds it and where, and the exceptions."
        actions={<>
          <AssignDialog roles={roles ?? []} members={members ?? []}
            nodes={(hierarchy?.nodes ?? []).map((node) => ({
              ...node,
              depth: node.depth,
            }))} />
          <FormDialog
            open={editorOpen}
            onOpenChange={setEditorOpen}
            trigger={<Button onClick={openCreate}>New role</Button>}
            title={editing ? 'Edit role' : 'New role'}
            description="Grants are additive; scope is chosen at assignment."
          >
            <RoleEditor
              key={editing?.id ?? 'new'}
              role={editing}
              onSaved={() => setEditorOpen(false)}
            />
          </FormDialog>
        </>}
      />

      <Grid
        columns={roleColumns}
        rows={roles ?? []}
        getRowId={(r) => r.id}
        emptyMessage='No roles yet. Create one with "New role".'
      />

      <ExceptionsCard members={members ?? []} nodes={(hierarchy?.nodes ?? []).map((node) => ({
        ...node,
        depth: node.depth,
      }))} />
    </div>
  );
}

/** Scoped assignment is the point: a role can apply to the whole org or one subtree. */
function AssignDialog({ roles, members, nodes }: { roles: Role[]; members: Member[]; nodes: Node[] }) {
  const [open, setOpen] = useState(false);
  const [roleId, setRoleId] = useState('');
  const [userId, setUserId] = useState('');
  const [scopePath, setScopePath] = useState('');

  const assign = useApiMutation({
    mutationFn: () =>
      rolesApi.assign(roleId, userId, scopePath || null),
    invalidate: [['roles'], ['members']],
    success: 'Role assigned',
    onSuccess: () => {
      setUserId('');
      setScopePath('');
      setOpen(false);
    },
  });

  return (
    <FormDialog
      open={open}
      onOpenChange={setOpen}
      trigger={<Button variant="outline">Assign role</Button>}
      title="Assign a role"
      description="Over the entire org, or scoped to one hierarchy subtree."
    >
      <div className="space-y-3">
        <Field>
          <FieldLabel htmlFor="assign-role">Role</FieldLabel>
          <Select id="assign-role" value={roleId} onChange={(e) => setRoleId(e.target.value)}>
            <option value="">Choose…</option>
            {roles.map((r) => (
              <option key={r.id} value={r.id}>{r.name}</option>
            ))}
          </Select>
        </Field>
        <Field>
          <FieldLabel htmlFor="assign-member">Member</FieldLabel>
          <Select id="assign-member" value={userId} onChange={(e) => setUserId(e.target.value)}>
            <option value="">Choose…</option>
            {members.map((m) => (
              <option key={m.userId} value={m.userId}>{m.email}</option>
            ))}
          </Select>
        </Field>
        <Field>
          <FieldLabel htmlFor="assign-scope">Scope</FieldLabel>
          <Select id="assign-scope" value={scopePath}
            onChange={(e) => setScopePath(e.target.value)}>
            <option value="">Entire org</option>
            {nodes.map((n) => (
              <option key={n.id} value={n.path}>
                {' '.repeat(n.depth * 2)}{n.name} subtree
              </option>
            ))}
          </Select>
        </Field>
        <Button className="w-full" disabled={!roleId || !userId || assign.isPending}
          onClick={() => assign.mutate()}>
          Assign
        </Button>
      </div>
    </FormDialog>
  );
}

/** Time-boxed additive exceptions (never deny): first-class and auditable. */
function ExceptionsCard({ members, nodes }: { members: Member[]; nodes: Node[] }) {
  const { data: exceptions } = useGrantExceptions();
  const [open, setOpen] = useState(false);
  const [userId, setUserId] = useState('');
  const [capability, setCapability] = useState('');
  const [reason, setReason] = useState('');
  const [days, setDays] = useState('7');
  const [scopePath, setScopePath] = useState('');

  const grant = useApiMutation({
    mutationFn: () => {
      const { domain, action } = parseGrant(capability);
      return rolesApi.addException(
        userId,
        domain,
        action,
        reason.trim(),
        new Date(Date.now() + Number(days) * 86400_000).toISOString(),
        scopePath || null,
      );
    },
    invalidate: [['grant-exceptions']],
    success: 'Exception granted',
    onSuccess: () => {
      setUserId('');
      setCapability('');
      setReason('');
      setOpen(false);
    },
  });
  const revoke = useApiMutation({
    mutationFn: rolesApi.removeException,
    invalidate: [['grant-exceptions']],
    success: 'Exception revoked',
  });
  type ExceptionRow = NonNullable<typeof exceptions>[number];
  const exceptionColumns = useMemo<ColumnDef<DataGridFeatures, ExceptionRow>[]>(
    () => [
      { id: 'email', accessorKey: 'email', header: 'Member' },
      {
        id: 'grant',
        header: 'Grant',
        cell: ({ row }) => (
          <span className="font-mono text-xs">
            {row.original.domain}:{row.original.action}
            {row.original.scopePath && <span className="text-muted-foreground"> (scoped)</span>}
          </span>
        ),
      },
      {
        id: 'reason',
        accessorKey: 'reason',
        header: 'Reason',
        cell: ({ row }) => <span className="block max-w-48 truncate text-muted-foreground">{row.original.reason}</span>,
      },
      {
        id: 'expires',
        accessorKey: 'expiresAt',
        header: 'Expires',
        cell: ({ row }) => <span className="text-muted-foreground">{fmtDate(row.original.expiresAt)}</span>,
      },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) => (
          <div className="text-right">
            <ConfirmButton size="sm" confirmLabel="Revoke this exception?" disabled={revoke.isPending} onConfirm={() => revoke.mutate(row.original.id)}>
              Revoke
            </ConfirmButton>
          </div>
        ),
        meta: { headerClassName: 'w-24' },
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  return (
    <Grid
      title="Grant exceptions"
      actions={
<FormDialog
            open={open}
            onOpenChange={setOpen}
            trigger={<Button variant="outline" size="sm">Grant exception</Button>}
            title="Grant an exception"
            description="Additive and time-boxed - it expires on its own, and the reason is part of the record."
          >
            <div className="space-y-3">
              <Field>
                <FieldLabel htmlFor="exc-member">Member</FieldLabel>
                <Select id="exc-member" value={userId}
                  onChange={(e) => setUserId(e.target.value)}>
                  <option value="">Choose…</option>
                  {members.map((m) => (
                    <option key={m.userId} value={m.userId}>{m.email}</option>
                  ))}
                </Select>
              </Field>
              <Field>
                <FieldLabel htmlFor="exc-cap">Capability</FieldLabel>
                <Select id="exc-cap" value={capability}
                  onChange={(e) => setCapability(e.target.value)}>
                  <option value="">Choose…</option>
                  {GRANTABLE.map((c) => (
                    <option key={c} value={c}>{capabilityLabel(c)}</option>
                  ))}
                </Select>
              </Field>
              <Field>
                <FieldLabel htmlFor="exc-scope">Scope</FieldLabel>
                <Select id="exc-scope" value={scopePath}
                  onChange={(e) => setScopePath(e.target.value)}>
                  <option value="">Entire org</option>
                  {nodes.map((n) => (
                    <option key={n.id} value={n.path}>
                      {' '.repeat(n.depth * 2)}{n.name} subtree
                    </option>
                  ))}
                </Select>
              </Field>
              <div className="grid grid-cols-[1fr_6rem] gap-2">
                <Field>
                  <FieldLabel htmlFor="exc-reason">Reason</FieldLabel>
                  <Input id="exc-reason" value={reason}
                    onChange={(e) => setReason(e.target.value)} />
                </Field>
                <Field>
                  <FieldLabel htmlFor="exc-days">Days</FieldLabel>
                  <Input id="exc-days" type="number" min="1" value={days}
                    onChange={(e) => setDays(e.target.value)} />
                </Field>
              </div>
              <Button className="w-full"
                disabled={!userId || !capability || !reason.trim() || !Number(days) || grant.isPending}
                onClick={() => grant.mutate()}>
                Grant
              </Button>
            </div>
          </FormDialog>
      }
      columns={exceptionColumns}
      rows={exceptions ?? []}
      getRowId={(x) => x.id}
      isLoading={exceptions === undefined}
      emptyMessage="No active exceptions."
    />
  );
}
