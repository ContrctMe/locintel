import { api, ApiError } from '@locintel/api';
import { Button, Field, FieldLabel, FormDialog, Input } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Loading, PageHeader, Panel } from '../components/page';
import { HierarchyTree } from '../features/hierarchy/hierarchy-tree';
import { useHierarchy } from '../features/hierarchy/hooks';
import { LevelsFields } from '../features/hierarchy/levels-fields';
import { NodePicker } from '../features/hierarchy/node-picker';
import { useApiMutation } from '../lib/mutation';
import { useMe } from '../session';

const trimmed = (levels: string[]) => levels.map((l) => l.trim()).filter(Boolean);

export function HierarchyPage() {
  const { data: me } = useMe();
  const { data, isPending, isError, error } = useHierarchy();
  // the plan's depth bounds both forms
  const { data: entitlements } = useQuery({
    queryKey: ['entitlements'],
    queryFn: ({ signal }) => api.get('/api/entitlements', { signal }),
  });
  const maxDepth = Number(entitlements?.['hierarchy.depth']?.value ?? 4);
  const [levels, setLevels] = useState(['Region', 'Market']);
  const [renaming, setRenaming] = useState(false);
  const [nodeName, setNodeName] = useState('');
  const [parentId, setParentId] = useState('');
  const [adding, setAdding] = useState(false);

  const provision = useApiMutation({
    mutationFn: () =>
      api.post('/api/hierarchy', { name: (me?.tier === 'user' && me.organizations.find((o) => o.id === me.activeOrg)?.name) || 'Organization', levels: trimmed(levels) }),
    invalidate: [['hierarchy']],
    success: 'Hierarchy created',
  });
  const renameLevels = useApiMutation({
    mutationFn: () => api.put('/api/hierarchy', { levels: trimmed(levels) }),
    invalidate: [['hierarchy']],
    success: 'Levels renamed',
    onSuccess: () => setRenaming(false),
  });
  const addNode = useApiMutation({
    mutationFn: () => api.post('/api/hierarchy/nodes', { parentId, name: nodeName }),
    invalidate: [['hierarchy']],
    success: 'Node added',
    onSuccess: () => {
      setNodeName('');
      setAdding(false);
    },
  });
  const rename = useApiMutation({
    mutationFn: (input: { id: string; name: string }) =>
      api.put('/api/hierarchy/nodes/{id}', { name: input.name }, { path: { id: input.id } }),
    invalidate: [['hierarchy']],
    success: 'Node renamed',
  });
  const removeNode = useApiMutation({
    mutationFn: (id: string) => api.del('/api/hierarchy/nodes/{id}', { path: { id } }),
    invalidate: [['hierarchy']],
    success: 'Node deleted',
    errorFallback: 'Delete failed',
  });

  if (isPending)
    return <Loading text="Loading hierarchy…" />;
  if (isError && !(error instanceof ApiError && error.status === 404))
    return <p className="text-sm text-destructive">Could not load hierarchy.</p>;

  if (!data) {
    return (
      <div className="max-w-lg space-y-6">
        <PageHeader title="Hierarchy" description="The rollup structure every site sits in." />
        <Panel
          title="Name your levels"
          description="The layers between the organization and a site: regions and markets, districts and stores. You can rename them later."
          bodyClassName="space-y-3"
        >
            <LevelsFields levels={levels} onChange={setLevels} max={maxDepth} />
            <Button disabled={trimmed(levels).length === 0 || provision.isPending} onClick={() => provision.mutate()}>
              Create hierarchy
            </Button>
            {provision.isError && (
              <p className="text-sm text-destructive">
                {String((provision.error as { body?: { error?: string } }).body?.error ?? 'failed')}
              </p>
            )}
        </Panel>
      </div>
    );
  }

  const deepestUsed = data.nodes.reduce((deep, n) => Math.max(deep, n.depth), 0);
  return (
    <div className="max-w-2xl space-y-6">
      <PageHeader
        title="Hierarchy"
        description={<>Levels: {data.levels.join(' → ')}</>}
        actions={
        <>
        <FormDialog
          open={renaming}
          onOpenChange={(open) => {
            setRenaming(open);
            if (open) setLevels([...data.levels]);
          }}
          trigger={<Button variant="outline">Rename levels</Button>}
          title="Rename levels"
          description="What each layer of the tree is called. Levels a node already uses stay; add more up to your plan's depth."
        >
          <div className="space-y-3">
            <LevelsFields levels={levels} onChange={setLevels} max={maxDepth} min={deepestUsed} />
            <Button
              className="w-full"
              disabled={trimmed(levels).length < Math.max(deepestUsed, 1) || renameLevels.isPending}
              onClick={() => renameLevels.mutate()}
            >
              Save levels
            </Button>
          </div>
        </FormDialog>
        <FormDialog
          open={adding}
          onOpenChange={setAdding}
          trigger={<Button>Add node</Button>}
          title="Add node"
          description="A new branch under an existing node."
        >
          <div className="space-y-3">
            <Field>
              <FieldLabel htmlFor="node-name">Name</FieldLabel>
              <Input id="node-name" value={nodeName}
                onChange={(e) => setNodeName(e.target.value)} />
            </Field>
            <Field>
              <FieldLabel htmlFor="node-parent">Parent</FieldLabel>
              <NodePicker id="node-parent" nodes={data.nodes} value={parentId} onChange={setParentId} />
            </Field>
            <Button className="w-full" disabled={!nodeName || !parentId || addNode.isPending}
              onClick={() => addNode.mutate()}>
              Add node
            </Button>
          </div>
        </FormDialog>
        </>
        }
      />
      <Panel>
        <HierarchyTree
          nodes={data.nodes.map((n) => ({
            id: n.id,
            name: n.name,
            depth: n.depth,
            parentId: n.parentId ?? null,
          }))}
          busy={rename.isPending || removeNode.isPending}
          onRename={(id, name) => rename.mutate({ id, name })}
          onDelete={(id) => removeNode.mutate(id)}
        />
      </Panel>
    </div>
  );
}
