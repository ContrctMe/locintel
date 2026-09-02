import { api, CAPABILITIES } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog,
  Input, Label, Select, Table, TableBody, TableCell, TableHead, TableHeader,
  TableRow } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { fmtDate } from '../lib/format';
import { useApiMutation } from '../lib/mutation';

type Grant = { domain: string; action: string };
type Role = { id: string; name: string; grants: Grant[]; assignedCount: number };
type Member = { userId: string; email: string; roles: string[] };
type Node = { id: string; name: string; depth: number; path: string };
type Hierarchy = { nodes: Node[] };
type GrantException = {
  id: string; email: string; domain: string; action: string;
  scopePath: string | null; reason: string; expiresAt: string;
};

const WILDCARD = '*:*';
// grantable pairs come from codegen (ADR 16); platform:operate is the
// operator wall - an org role can hold it, but it only bites in the platform org
const GRANTABLE = CAPABILITIES.filter((c) => c !== 'public:read' && c !== 'platform:operate');

const grantKey = (g: Grant) => `${g.domain}:${g.action}`;

/** The role editor: what a role grants, who holds it and where, and the exceptions. */
export function RolesPage() {
  const { data: roles } = useQuery({
    queryKey: ['roles'],
    queryFn: () => api.get<Role[]>('/api/roles'),
  });
  const { data: members } = useQuery({
    queryKey: ['members', 'picker'],
    queryFn: async () =>
      (await api.get<{ items: Member[] }>('/api/members?limit=200')).items,
  });
  const { data: hierarchy } = useQuery({
    queryKey: ['hierarchy'],
    queryFn: () => api.get<Hierarchy>('/api/hierarchy'),
    retry: false,
  });

  const [editorOpen, setEditorOpen] = useState(false);
  const [editing, setEditing] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [picked, setPicked] = useState<Set<string>>(new Set());

  const openCreate = () => {
    setEditing(null);
    setName('');
    setPicked(new Set());
    setEditorOpen(true);
  };
  const openEdit = (role: Role) => {
    setEditing(role.id);
    setName(role.name);
    setPicked(new Set(role.grants.map(grantKey)));
    setEditorOpen(true);
  };

  const save = useApiMutation({
    mutationFn: () => {
      const grants = [...picked].map((key) => {
        const [domain = '', action = ''] = key.split(':');
        return { domain, action };
      });
      return editing
        ? api.put(`/api/roles/${editing}`, { name: name.trim(), grants })
        : api.post('/api/roles', { name: name.trim(), grants });
    },
    invalidate: [['roles']],
    success: 'Role saved',
    errorFallback: 'Save failed',
    onSuccess: () => setEditorOpen(false),
  });
  const remove = useApiMutation({
    mutationFn: (id: string) => api.del(`/api/roles/${id}`),
    invalidate: [['roles']],
    success: 'Role deleted',
    errorFallback: 'Delete failed',
  });

  const toggle = (key: string) => {
    const next = new Set(picked);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    setPicked(next);
  };

  return (
    <div className="max-w-4xl space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Roles</h1>
        <div className="flex gap-2">
          <AssignDialog roles={roles ?? []} members={members ?? []}
            nodes={hierarchy?.nodes ?? []} />
          <FormDialog
            open={editorOpen}
            onOpenChange={setEditorOpen}
            trigger={<Button onClick={openCreate}>New role</Button>}
            title={editing ? 'Edit role' : 'New role'}
            description="Grants are additive; scope is chosen at assignment."
          >
            <div className="space-y-3">
              <div className="space-y-1">
                <Label htmlFor="role-name">Name</Label>
                <Input id="role-name" value={name} onChange={(e) => setName(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label>Grants</Label>
                <div className="grid grid-cols-2 gap-1.5">
                  <label className="flex items-center gap-2 text-sm">
                    <input type="checkbox" checked={picked.has(WILDCARD)}
                      onChange={() => toggle(WILDCARD)} />
                    <span className="font-mono">*:* (everything)</span>
                  </label>
                  {GRANTABLE.map((c) => (
                    <label key={c} className="flex items-center gap-2 text-sm">
                      <input type="checkbox" checked={picked.has(c)} onChange={() => toggle(c)} />
                      <span className="font-mono">{c}</span>
                    </label>
                  ))}
                </div>
              </div>
              <Button className="w-full"
                disabled={!name.trim() || picked.size === 0 || save.isPending}
                onClick={() => save.mutate()}>
                {editing ? 'Save changes' : 'Create role'}
              </Button>
            </div>
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

      <ExceptionsCard members={members ?? []} nodes={hierarchy?.nodes ?? []} />
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
      api.post(`/api/roles/${roleId}/assign`, { userId, scopePath: scopePath || null }),
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
  const { data: exceptions } = useQuery({
    queryKey: ['grant-exceptions'],
    queryFn: () => api.get<GrantException[]>('/api/grant-exceptions'),
  });
  const [open, setOpen] = useState(false);
  const [userId, setUserId] = useState('');
  const [capability, setCapability] = useState('');
  const [reason, setReason] = useState('');
  const [days, setDays] = useState('7');
  const [scopePath, setScopePath] = useState('');

  const grant = useApiMutation({
    mutationFn: () => {
      const [domain = '', action = ''] = capability.split(':');
      return api.post('/api/grant-exceptions', {
        userId,
        domain,
        action,
        reason: reason.trim(),
        expiresAt: new Date(Date.now() + Number(days) * 86400_000).toISOString(),
        scopePath: scopePath || null,
      });
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
    mutationFn: (id: string) => api.del(`/api/grant-exceptions/${id}`),
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
