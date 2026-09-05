import { checklistsApi } from './api';
import { useSites } from '../sites';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog,
  Input, Label, Select } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApiMutation } from '../../lib/mutation';
import { can, useMe } from '../../session';

/** The ops core loop (ADR 45): today's lists per site, on the site's clock. */
export function ChecklistsPage() {
  const { data: me } = useMe();
  const manage = can(me, 'checklists:manage');
  const [siteId, setSiteId] = useState('');

  const siteQuery = useSites('');
  const sites = siteQuery.data?.pages.flatMap((page) => page.items);
  const activeSite = siteId || sites?.[0]?.id || '';
  const todayQuery = useQuery({
    queryKey: ['checklists', 'today', activeSite],
    queryFn: ({ signal }) => checklistsApi.today(activeSite, signal),
    enabled: !!activeSite,
  });
  const today = todayQuery.data;
  const check = useApiMutation({
    mutationFn: (input: { templateId: string; itemIndex: number; done: boolean }) =>
      checklistsApi.check({ ...input, siteId: activeSite }),
    invalidate: [['checklists', 'today', activeSite]],
  });

  return (
    <div className="max-w-3xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Checklists</h1>
        {sites && sites.length > 1 && (
          <Select aria-label="Checklist site" className="w-56" value={activeSite}
            onChange={(e) => setSiteId(e.target.value)}>
            {sites.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </Select>
        )}
      </div>
      {siteQuery.isPending && <p role="status">Loading sites…</p>}
      {siteQuery.isError && <div role="alert">Could not load sites. <Button onClick={() => {
        if (siteQuery.isFetchNextPageError) void siteQuery.fetchNextPage();
        else void siteQuery.refetch();
      }}>Retry sites</Button></div>}
      {siteQuery.hasNextPage && !siteQuery.isError && <Button disabled={siteQuery.isFetchingNextPage}
        onClick={() => void siteQuery.fetchNextPage()}>
        {siteQuery.isFetchingNextPage ? 'Loading more sites…' : 'Load more sites'}
      </Button>}
      {sites?.length === 0 && !siteQuery.isError && <p>No accessible sites yet.</p>}
      {activeSite && todayQuery.isPending && <p role="status">Loading checklists…</p>}
      {todayQuery.isError && <div role="alert">Could not load checklists. <Button
        onClick={() => void todayQuery.refetch()}>Retry checklists</Button></div>}
      {today && (
        <p className="text-sm text-muted-foreground">
          {today.site} · {today.businessDate} (site-local day)
        </p>
      )}
      {today?.lists.length === 0 && (
        <Card>
          <CardContent className="pt-4 text-sm text-muted-foreground">
            No checklists apply to this site yet.
            {manage && ' Create a template below.'}
          </CardContent>
        </Card>
      )}
      {today?.lists.map((list) => {
        const done = list.items.filter((i) => i.done).length;
        return (
          <Card key={list.id}>
            <CardHeader>
              <CardTitle className="flex items-center justify-between text-base">
                {list.name}
                <span className="text-sm font-normal text-muted-foreground">
                  {done}/{list.items.length}
                </span>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <ul className="space-y-2">
                {list.items.map((item) => (
                  <li key={item.index}>
                    <label className="flex cursor-pointer items-center gap-3 text-sm">
                      <input
                        type="checkbox"
                        className="size-4 accent-primary"
                        checked={item.done}
                        disabled={check.isPending}
                        onChange={(e) =>
                          check.mutate({
                            templateId: list.id,
                            itemIndex: Number(item.index),
                            done: e.target.checked,
                          })
                        }
                      />
                      <span className={item.done ? 'text-muted-foreground line-through' : ''}>
                        {item.text}
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            </CardContent>
          </Card>
        );
      })}
      {manage && <TemplatesCard />}
    </div>
  );
}

function TemplatesCard() {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [items, setItems] = useState('');

  const templatesQuery = useQuery({
    queryKey: ['checklists', 'templates'],
    queryFn: ({ signal }) => checklistsApi.templates(signal),
  });
  const templates = templatesQuery.data;
  const create = useApiMutation({
    mutationFn: () =>
      checklistsApi.create({
        name: name.trim(),
        items: items.split('\n').map((i) => i.trim()).filter(Boolean),
      }),
    invalidate: [['checklists']],
    success: 'Checklist created',
    onSuccess: () => {
      setOpen(false);
      setName('');
      setItems('');
    },
  });
  const remove = useApiMutation({
    mutationFn: checklistsApi.remove,
    invalidate: [['checklists']],
    success: 'Checklist deleted',
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between text-base">
          Templates
          <FormDialog
            open={open}
            onOpenChange={setOpen}
            trigger={<Button size="sm">New checklist</Button>}
            title="New checklist"
            description="Applies daily at every site. One item per line."
          >
            <div className="space-y-3">
              <div className="space-y-1">
                <Label htmlFor="cl-name">Name</Label>
                <Input id="cl-name" value={name} placeholder="Opening"
                  onChange={(e) => setName(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="cl-items">Items</Label>
                <textarea
                  id="cl-items"
                  className="min-h-28 w-full rounded-md border bg-background px-3 py-2 text-sm"
                  value={items}
                  placeholder={'Unlock doors\nCount register'}
                  onChange={(e) => setItems(e.target.value)}
                />
              </div>
              <Button className="w-full"
                disabled={!name.trim() || !items.trim() || create.isPending}
                onClick={() => create.mutate()}>
                Create
              </Button>
            </div>
          </FormDialog>
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {templatesQuery.isPending && <p role="status">Loading templates…</p>}
        {templatesQuery.isError && <div role="alert">Could not load templates. <Button
          onClick={() => void templatesQuery.refetch()}>Retry templates</Button></div>}
        {templates?.length === 0 && (
          <p className="text-sm text-muted-foreground">No templates yet.</p>
        )}
        {templates?.map((t) => (
          <div key={t.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
            <span>
              <span className="font-medium">{t.name}</span>
              <span className="ml-2 text-muted-foreground">{t.items.length} items</span>
            </span>
            <ConfirmButton size="sm" variant="ghost" disabled={remove.isPending}
              onConfirm={() => remove.mutate(t.id)}>
              Delete
            </ConfirmButton>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
