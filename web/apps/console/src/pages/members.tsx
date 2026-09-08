import { api } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog,
  Input, Label, Select, Table, TableBody, TableCell, TableHead, TableHeader,
  TableRow } from '@locintel/ui';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { fmtDate } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import { useMe } from '../session';

export function MembersPage() {
  const { data: me } = useMe();
  const membersQuery = useInfiniteQuery({
    queryKey: ['members', 'list'],
    queryFn: ({ pageParam, signal }) =>
      api.get('/api/members', { query: { limit: 50, offset: pageParam }, signal }),
    initialPageParam: 0,
    getNextPageParam: (last) =>
      last.nextOffset == null ? undefined : Number(last.nextOffset),
  });
  const members = membersQuery.data?.pages.flatMap((p) => p.items);
  const { data: roles } = useQuery({
    queryKey: ['roles'],
    queryFn: ({ signal }) => api.get('/api/roles', { signal }),
  });
  const { data: invitations } = useQuery({
    queryKey: ['invitations'],
    queryFn: ({ signal }) => api.get('/api/members/invitations', { signal }),
  });
  const { data: contacts } = useQuery({
    queryKey: ['contacts'],
    queryFn: ({ signal }) => api.get('/api/contacts', { signal }),
  });
  const [email, setEmail] = useState('');
  const [roleId, setRoleId] = useState('');
  const [inviting, setInviting] = useState(false);
  const [contactEmail, setContactEmail] = useState('');
  const [invitingContact, setInvitingContact] = useState(false);

  const invite = useApiMutation({
    mutationFn: () => api.post('/api/members/invitations', { email, roleId }),
    invalidate: [['invitations']],
    success: 'Invitation sent',
    onSuccess: () => {
      setEmail('');
      setInviting(false);
    },
  });
  const revoke = useApiMutation({
    mutationFn: (id: string) => api.del('/api/members/invitations/{invitationId}', { path: { invitationId: id } }),
    invalidate: [['invitations']],
    success: 'Invitation revoked',
  });
  const remove = useApiMutation({
    mutationFn: (userId: string) => api.del('/api/members/{userId}', { path: { userId } }),
    invalidate: [['members']],
    success: 'Member removed',
    errorFallback: 'Removal failed',
  });
  const unassign = useApiMutation({
    mutationFn: (input: { roleName: string; userId: string }) => {
      const role = roles?.find((r) => r.name === input.roleName);
      if (!role) throw new Error('unknown role');
      return api.del('/api/roles/{id}/assign/{userId}', { path: { id: role.id, userId: input.userId } });
    },
    invalidate: [['members']],
    success: 'Role unassigned',
    errorFallback: 'Unassign failed',
  });

  // closes the review's P2-flagged gap while we're here: the contact journey
  // can finally START from the console
  const inviteContact = useApiMutation({
    mutationFn: () => api.post('/contact-links', { email: contactEmail }),
    invalidate: [['contacts']],
    success: 'Contact link sent',
    onSuccess: () => {
      setContactEmail('');
      setInvitingContact(false);
    },
  });
  const revokeContact = useApiMutation({
    mutationFn: (id: string) => api.del('/api/contacts/{id}', { path: { id } }),
    invalidate: [['contacts']],
    success: 'Contact revoked',
  });

  const self = me?.tier === 'user' ? me.userId : undefined;

  return (
    <div className="max-w-4xl space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Members</h1>
        <div className="flex gap-2">
          <FormDialog
            open={invitingContact}
            onOpenChange={setInvitingContact}
            trigger={<Button variant="outline">Invite a contact</Button>}
            title="Invite a contact"
            description="Contacts get an identified link to your public pages - no account, revocable any time."
          >
            <div className="space-y-3">
              <div className="space-y-1">
                <Label htmlFor="contact-email">Email</Label>
                <Input id="contact-email" type="email" value={contactEmail}
                  onChange={(e) => setContactEmail(e.target.value)} />
              </div>
              <Button className="w-full"
                disabled={!contactEmail.includes('@') || inviteContact.isPending}
                onClick={() => inviteContact.mutate()}>
                Send contact link
              </Button>
            </div>
          </FormDialog>
          <FormDialog
            open={inviting}
            onOpenChange={setInviting}
            trigger={<Button>Invite a member</Button>}
            title="Invite a member"
            description="They join with the role you pick, delivered by your identity provider."
          >
            <div className="space-y-3">
              <div className="space-y-1">
                <Label htmlFor="invite-email">Email</Label>
                <Input id="invite-email" type="email" value={email}
                  onChange={(e) => setEmail(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="invite-role">Role</Label>
                <Select id="invite-role" value={roleId}
                  onChange={(e) => setRoleId(e.target.value)}>
                  <option value="">Choose…</option>
                  {roles?.map((r) => (
                    <option key={r.id} value={r.id}>{r.name}</option>
                  ))}
                </Select>
              </div>
              <Button className="w-full"
                disabled={!email.includes('@') || !roleId || invite.isPending}
                onClick={() => invite.mutate()}>
                Send invite
              </Button>
            </div>
          </FormDialog>
        </div>
      </div>

      <Card>
        <CardContent className="pt-4">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Member</TableHead>
                <TableHead>Roles</TableHead>
                <TableHead>Joined</TableHead>
                <TableHead><span className="sr-only">Actions</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {members === undefined && (
                <TableRow>
                  <TableCell colSpan={4} className="text-center text-muted-foreground">
                    Loading…
                  </TableCell>
                </TableRow>
              )}
              {members?.map((m) => (
                <TableRow key={m.userId}>
                  <TableCell>
                    <div className="font-medium">{m.name ?? m.email}</div>
                    <div className="text-xs text-muted-foreground">{m.email}</div>
                  </TableCell>
                  <TableCell>
                    {m.roles.length === 0
                      ? '—'
                      : m.roles.map((roleName) => (
                          <span key={roleName}
                            className="mr-1 inline-flex items-center gap-1 rounded-full bg-secondary px-2 py-0.5 text-xs">
                            {roleName}
                            <ConfirmButton
                              size="sm"
                              variant="ghost"
                              className="h-4 px-1 text-xs"
                              confirmLabel="Unassign?"
                              disabled={unassign.isPending}
                              onConfirm={() => unassign.mutate({ roleName, userId: m.userId })}
                            >
                              ×
                            </ConfirmButton>
                          </span>
                        ))}
                  </TableCell>
                  <TableCell className="text-muted-foreground">{fmtDate(m.joinedAt)}</TableCell>
                  <TableCell className="text-right">
                    {m.userId !== self && (
                      <ConfirmButton
                        size="sm"
                        disabled={remove.isPending}
                        onConfirm={() => remove.mutate(m.userId)}
                      >
                        Remove
                      </ConfirmButton>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {membersQuery.hasNextPage && (
            <div className="pt-3 text-center">
              <Button variant="outline" size="sm"
                disabled={membersQuery.isFetchingNextPage}
                onClick={() => void membersQuery.fetchNextPage()}>
                Load more
              </Button>
            </div>
          )}
        </CardContent>
      </Card>

      {invitations && invitations.length > 0 && (
        <Card>
          <CardHeader><CardTitle>Pending invitations</CardTitle></CardHeader>
          <CardContent>

            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Pending</TableHead>
                  <TableHead>Role</TableHead>
                  <TableHead>State</TableHead>
                  <TableHead><span className="sr-only">Actions</span></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {invitations.map((inv) => (
                  <TableRow key={inv.id}>
                    <TableCell>{inv.email}</TableCell>
                    <TableCell>{inv.role ?? '—'}</TableCell>
                    <TableCell className="text-muted-foreground">{inv.state}</TableCell>
                    <TableCell className="text-right">
                      {inv.state === 'pending' && (
                        <ConfirmButton
                          size="sm"
                          disabled={revoke.isPending}
                          onConfirm={() => revoke.mutate(inv.id)}
                        >
                          Revoke
                        </ConfirmButton>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader><CardTitle>Contacts</CardTitle></CardHeader>
        <CardContent>
          <p className="mb-3 text-sm text-muted-foreground">
            People given identified access to your public pages via contact links.
            Revoking cuts off live sessions and unexpired links at once.
          </p>
          {contacts && contacts.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Email</TableHead>
                  <TableHead>Since</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead><span className="sr-only">Actions</span></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {contacts.map((c) => (
                  <TableRow key={c.id}>
                    <TableCell>{c.email}</TableCell>
                    <TableCell className="text-muted-foreground">{fmtDate(c.createdAt)}</TableCell>
                    <TableCell className="text-muted-foreground">
                      {c.revoked ? 'Revoked' : 'Active'}
                    </TableCell>
                    <TableCell className="text-right">
                      {!c.revoked && (
                        <ConfirmButton
                          size="sm"
                          disabled={revokeContact.isPending}
                          onConfirm={() => revokeContact.mutate(c.id)}
                        >
                          Revoke
                        </ConfirmButton>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <p className="text-sm text-muted-foreground">No contacts yet.</p>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
