import { api } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog,
  Input, Label, Select } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApiMutation } from '../lib/mutation';

type Node = { id: string; parentId?: string; name: string; depth: number; path: string };
type Hierarchy = { id: string; name: string; levels: string[]; nodes: Node[] };

export function HierarchyPage() {
  const { data, isError } = useQuery({
    queryKey: ['hierarchy'],
    queryFn: () => api.get<Hierarchy>('/api/hierarchy'),
    retry: false,
  });
  const [levels, setLevels] = useState('Region, Market');
  const [nodeName, setNodeName] = useState('');
  const [parentId, setParentId] = useState('');
  const [adding, setAdding] = useState(false);

  const provision = useApiMutation({
    mutationFn: () =>
      api.post('/api/hierarchy', {
        name: 'Organization',
        levels: levels.split(',').map((l) => l.trim()).filter(Boolean),
      }),
    invalidate: [['hierarchy']],
    success: 'Hierarchy created',
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
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const rename = useApiMutation({
    mutationFn: (input: { id: string; name: string }) =>
      api.put(`/api/hierarchy/nodes/${input.id}`, { name: input.name }),
    invalidate: [['hierarchy']],
    success: 'Node renamed',
    onSuccess: () => setEditingId(null),
  });
  const removeNode = useApiMutation({
    mutationFn: (id: string) => api.del(`/api/hierarchy/nodes/${id}`),
    invalidate: [['hierarchy']],
    success: 'Node deleted',
    errorFallback: 'Delete failed',
  });

  if (isError || !data) {
    return (
      <div className="max-w-lg space-y-6">
        <h1 className="text-2xl font-semibold">Hierarchy</h1>
        <Card>
          <CardHeader><CardTitle>Provision the org hierarchy</CardTitle></CardHeader>
          <CardContent className="space-y-3">
            <div className="space-y-1">
              <Label htmlFor="levels">Level names (root-first, comma-separated)</Label>
              <Input id="levels" value={levels} onChange={(e) => setLevels(e.target.value)} />
            </div>
            <Button disabled={provision.isPending} onClick={() => provision.mutate()}>
              Create hierarchy
            </Button>
            {provision.isError && (
              <p className="text-sm text-destructive">
                {String((provision.error as { body?: { error?: string } }).body?.error ?? 'failed')}
              </p>
            )}
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="max-w-2xl space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Hierarchy</h1>
        <FormDialog
          open={adding}
          onOpenChange={setAdding}
          trigger={<Button>Add node</Button>}
          title="Add node"
          description="A new branch under an existing node."
        >
          <div className="space-y-3">
            <div className="space-y-1">
              <Label htmlFor="node-name">Name</Label>
              <Input id="node-name" value={nodeName}
                onChange={(e) => setNodeName(e.target.value)} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="node-parent">Parent</Label>
              <Select id="node-parent" value={parentId}
                onChange={(e) => setParentId(e.target.value)}>
                <option value="">Choose…</option>
                {data.nodes.map((n) => (
                  <option key={n.id} value={n.id}>
                    {' '.repeat(n.depth * 2)}{n.name}
                  </option>
                ))}
              </Select>
            </div>
            <Button className="w-full" disabled={!nodeName || !parentId || addNode.isPending}
              onClick={() => addNode.mutate()}>
              Add node
            </Button>
          </div>
        </FormDialog>
      </div>
      <p className="text-sm text-muted-foreground">Levels: {data.levels.join(' → ')}</p>
      <Card>
        <CardContent className="pt-4">
          <ul className="space-y-1 text-sm">
            {data.nodes.map((n) => {
              const isLeaf =
                !data.nodes.some((c) => c.parentId === n.id) && n.depth > 0;
              return (
                <li
                  key={n.id}
                  className="group flex items-center gap-2"
                  style={{ paddingLeft: `${n.depth * 1.25}rem` }}
                >
                  {editingId === n.id ? (
                    <>
                      <Input
                        className="h-7 w-48"
                        value={editName}
                        autoFocus
                        onChange={(e) => setEditName(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter' && editName.trim())
                            rename.mutate({ id: n.id, name: editName.trim() });
                          if (e.key === 'Escape') setEditingId(null);
                        }}
                      />
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={!editName.trim() || rename.isPending}
                        onClick={() => rename.mutate({ id: n.id, name: editName.trim() })}
                      >
                        Save
                      </Button>
                    </>
                  ) : (
                    <>
                      <span className="font-mono">
                        {n.depth > 0 ? '└ ' : ''}{n.name}
                      </span>
                      <span className="flex gap-1 opacity-50 transition-opacity focus-within:opacity-100 group-hover:opacity-100">
                        <Button
                          variant="ghost"
                          size="sm"
                          className="h-6 px-2 text-xs"
                          onClick={() => {
                            setEditingId(n.id);
                            setEditName(n.name);
                          }}
                        >
                          Rename
                        </Button>
                        {isLeaf && (
                          <ConfirmButton
                            size="sm"
                            className="h-6 px-2 text-xs"
                            disabled={removeNode.isPending}
                            onConfirm={() => removeNode.mutate(n.id)}
                          >
                            Delete
                          </ConfirmButton>
                        )}
                      </span>
                    </>
                  )}
                </li>
              );
            })}
          </ul>
        </CardContent>
      </Card>
    </div>
  );
}
