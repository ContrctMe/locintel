import { type components } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog,
  Input, Label, Select, Table, TableBody, TableCell, TableHead, TableHeader,
  TableRow } from '@locintel/ui';
import { useState } from 'react';
import { fmtDate } from '../../../lib/format';
import { useApiMutation } from '../../../lib/mutation';
import { rolesApi } from '../api';
import { useGrantExceptions, useRoleHierarchy, useRoleMembers, useRoles } from '../hooks';
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

  if (rolesQuery.isPending || membersQuery.isPending || hierarchyQuery.isPending)
    return <p className="text-sm text-muted-foreground">Loading roles…</p>;
  if (rolesQuery.isError || membersQuery.isError)
    return <p className="text-sm text-destructive">Could not load roles.</p>;

  return (
    <div className="max-w-4xl space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Roles</h1>
        <div className="flex gap-2">
          <AssignDialog roles={roles ?? []} members={members ?? []}
            nodes={(hierarchy?.nodes ?? []).map((node) => ({
              ...node,
              depth: Number(node.depth),
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
        </div>
      </div>

      <Card>
        <CardContent className="pt-4">
          {roles && roles.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Role</TableHead>
                  <TableHead>Grants</TableHead>
                  <TableHead>Held by</TableHead>
                  <TableHead className="w-40"><span className="sr-only">Actions</span></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {roles.map((r) => (
                  <TableRow key={r.id}>
                    <TableCell className="font-medium">{r.name}</TableCell>
                    <TableCell>
                      <div className="flex max-w-md flex-wrap gap-1">
                        {r.grants.map((g) => (
                          <span key={grantKey(g)}
                            className="rounded bg-accent px-1.5 py-0.5 font-mono text-xs">
                            {grantKey(g)}
                          </span>
                        ))}
                      </div>
                    </TableCell>
                    <TableCell className="text-muted-foreground">{r.assignedCount}</TableCell>
                    <TableCell className="space-x-1 text-right">
                      <Button variant="ghost" size="sm" onClick={() => openEdit(r)}>
                        Edit
                      </Button>
                      <ConfirmButton size="sm" disabled={remove.isPending}
                        onConfirm={() => remove.mutate(r.id)}>
                        Delete
                      </ConfirmButton>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <p className="text-sm text-muted-foreground">
              No roles yet. Create one with &quot;New role&quot;.
            </p>
          )}
        </CardContent>
      </Card>

      <ExceptionsCard members={members ?? []} nodes={(hierarchy?.nodes ?? []).map((node) => ({
        ...node,
        depth: Number(node.depth),
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
        <div className="space-y-1">
          <Label htmlFor="assign-role">Role</Label>
          <Select id="assign-role" value={roleId} onChange={(e) => setRoleId(e.target.value)}>
            <option value="">Choose…</option>
            {roles.map((r) => (
              <option key={r.id} value={r.id}>{r.name}</option>
            ))}
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="assign-member">Member</Label>
          <Select id="assign-member" value={userId} onChange={(e) => setUserId(e.target.value)}>
            <option value="">Choose…</option>
            {members.map((m) => (
              <option key={m.userId} value={m.userId}>{m.email}</option>
            ))}
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="assign-scope">Scope</Label>
          <Select id="assign-scope" value={scopePath}
            onChange={(e) => setScopePath(e.target.value)}>
            <option value="">Entire org</option>
            {nodes.map((n) => (
              <option key={n.id} value={n.path}>
                {' '.repeat(n.depth * 2)}{n.name} subtree
              </option>
            ))}
          </Select>
        </div>
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

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between">
          Grant exceptions
          <FormDialog
            open={open}
            onOpenChange={setOpen}
            trigger={<Button variant="outline" size="sm">Grant exception</Button>}
            title="Grant an exception"
            description="Additive and time-boxed - it expires on its own, and the reason is part of the record."
          >
            <div className="space-y-3">
              <div className="space-y-1">
                <Label htmlFor="exc-member">Member</Label>
                <Select id="exc-member" value={userId}
                  onChange={(e) => setUserId(e.target.value)}>
                  <option value="">Choose…</option>
                  {members.map((m) => (
                    <option key={m.userId} value={m.userId}>{m.email}</option>
                  ))}
                </Select>
              </div>
              <div className="space-y-1">
                <Label htmlFor="exc-cap">Capability</Label>
                <Select id="exc-cap" value={capability}
                  onChange={(e) => setCapability(e.target.value)}>
                  <option value="">Choose…</option>
                  {GRANTABLE.map((c) => (
                    <option key={c} value={c}>{c}</option>
                  ))}
                </Select>
              </div>
              <div className="space-y-1">
                <Label htmlFor="exc-scope">Scope</Label>
                <Select id="exc-scope" value={scopePath}
                  onChange={(e) => setScopePath(e.target.value)}>
                  <option value="">Entire org</option>
                  {nodes.map((n) => (
                    <option key={n.id} value={n.path}>
                      {' '.repeat(n.depth * 2)}{n.name} subtree
                    </option>
                  ))}
                </Select>
              </div>
              <div className="grid grid-cols-[1fr_6rem] gap-2">
                <div className="space-y-1">
                  <Label htmlFor="exc-reason">Reason</Label>
                  <Input id="exc-reason" value={reason}
                    onChange={(e) => setReason(e.target.value)} />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="exc-days">Days</Label>
                  <Input id="exc-days" type="number" min="1" value={days}
                    onChange={(e) => setDays(e.target.value)} />
                </div>
              </div>
              <Button className="w-full"
                disabled={!userId || !capability || !reason.trim() || !Number(days) || grant.isPending}
                onClick={() => grant.mutate()}>
                Grant
              </Button>
            </div>
          </FormDialog>
        </CardTitle>
      </CardHeader>
      <CardContent>
        {exceptions && exceptions.length > 0 ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Member</TableHead>
                <TableHead>Grant</TableHead>
                <TableHead>Reason</TableHead>
                <TableHead>Expires</TableHead>
                <TableHead className="w-24"><span className="sr-only">Actions</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {exceptions.map((x) => (
                <TableRow key={x.id}>
                  <TableCell>{x.email}</TableCell>
                  <TableCell className="font-mono text-xs">
                    {x.domain}:{x.action}
                    {x.scopePath && <span className="text-muted-foreground"> (scoped)</span>}
                  </TableCell>
                  <TableCell className="max-w-48 truncate text-muted-foreground">
                    {x.reason}
                  </TableCell>
                  <TableCell className="text-muted-foreground">{fmtDate(x.expiresAt)}</TableCell>
                  <TableCell className="text-right">
                    <ConfirmButton size="sm" disabled={revoke.isPending}
                      onConfirm={() => revoke.mutate(x.id)}>
                      Revoke
                    </ConfirmButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p className="text-sm text-muted-foreground">No active exceptions.</p>
        )}
      </CardContent>
    </Card>
  );
}
