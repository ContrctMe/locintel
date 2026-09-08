import { Button, ConfirmButton, Input, Tree, TreeItem, TreeItemLabel } from '@locintel/ui';
import { hotkeysCoreFeature, syncDataLoaderFeature } from '@headless-tree/core';
import { useTree } from '@headless-tree/react';
import { useMemo, useState } from 'react';

const VIRTUAL_ROOT = '__hierarchy_root__';

export type HierarchyTreeNode = {
  id: string;
  name: string;
  depth: number;
  parentId: string | null;
};

/**
 * The org hierarchy as the ReUI Tree (headless-tree underneath): expand and
 * collapse, keyboard navigation, indent guides. Rename is inline on the
 * selected row; delete is offered on leaves only, because nodes are
 * restructured or absorbed, never soft-deleted (ADR 25).
 */
export function HierarchyTree({
  nodes,
  onRename,
  onDelete,
  busy,
}: {
  nodes: HierarchyTreeNode[];
  onRename: (id: string, name: string) => void;
  onDelete: (id: string) => void;
  busy: boolean;
}) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');

  // one lookup the tree's data loader reads from. headless-tree renders a
  // root's CHILDREN, never the root itself, so a virtual root holds the
  // org's top-level nodes and every real node, the root included, is a row.
  const { byId, childrenOf } = useMemo(() => {
    const byId = new Map<string, HierarchyTreeNode>(nodes.map((n) => [n.id, n]));
    byId.set(VIRTUAL_ROOT, { id: VIRTUAL_ROOT, name: '', depth: -1, parentId: null });
    const childrenOf = new Map<string, string[]>();
    for (const n of nodes) {
      const parent = n.parentId ?? VIRTUAL_ROOT;
      childrenOf.set(parent, [...(childrenOf.get(parent) ?? []), n.id]);
    }
    return { byId, childrenOf };
  }, [nodes]);

  const tree = useTree<HierarchyTreeNode>({
    // the top level open, everything below it closed: what is on screen is
    // what was asked for, however many markets a region has (ADR 51's
    // console half - the DOM is bounded by expansion, not by the org)
    initialState: { expandedItems: nodes.filter((n) => n.depth === 0).map((n) => n.id) },
    indent: 20,
    rootItemId: VIRTUAL_ROOT,
    getItemName: (item) => item.getItemData()?.name ?? '',
    isItemFolder: (item) => (childrenOf.get(item.getId())?.length ?? 0) > 0,
    dataLoader: {
      getItem: (id) => byId.get(id) as HierarchyTreeNode,
      getChildren: (id) => childrenOf.get(id) ?? [],
    },
    features: [syncDataLoaderFeature, hotkeysCoreFeature],
  });

  return (
    <Tree
      className="relative before:absolute before:inset-0 before:-ms-1 before:bg-[repeating-linear-gradient(to_right,transparent_0,transparent_calc(var(--tree-indent)-1px),var(--border)_calc(var(--tree-indent)-1px),var(--border)_calc(var(--tree-indent)))]"
      indent={20}
      tree={tree}
    >
      {tree.getItems().map((item) => {
        const node = item.getItemData();
        if (!node) return null;
        const isLeaf = !item.isFolder() && node.depth > 0;
        return (
          <TreeItem key={item.getId()} item={item} className="group/node">
            <TreeItemLabel className="gap-2 pr-1">
              {editingId === node.id ? (
                <span className="ml-auto flex items-center gap-2" onClick={(e) => e.stopPropagation()}>
                  <Input
                    className="h-7 w-48"
                    value={editName}
                    autoFocus
                    onChange={(e) => setEditName(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' && editName.trim()) {
                        onRename(node.id, editName.trim());
                        setEditingId(null);
                      }
                      if (e.key === 'Escape') setEditingId(null);
                    }}
                  />
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={!editName.trim() || busy}
                    onClick={() => {
                      onRename(node.id, editName.trim());
                      setEditingId(null);
                    }}
                  >
                    Save
                  </Button>
                </span>
              ) : (
                <>
                  <span className="truncate">{node.name}</span>
                  <span
                    className="ml-auto flex shrink-0 gap-1 opacity-0 transition-opacity focus-within:opacity-100 group-hover/node:opacity-100 [@media(hover:none)]:opacity-100"
                    onClick={(e) => e.stopPropagation()}
                  >
                    <Button
                      variant="ghost"
                      size="xs"
                      onClick={() => {
                        setEditingId(node.id);
                        setEditName(node.name);
                      }}
                    >
                      Rename
                    </Button>
                    {isLeaf && (
                      <ConfirmButton
                        size="xs"
                        confirmLabel="Delete this node?"
                        description={`${node.name} has no children and no sites under it.`}
                        disabled={busy}
                        onConfirm={() => onDelete(node.id)}
                      >
                        Delete
                      </ConfirmButton>
                    )}
                  </span>
                </>
              )}
            </TreeItemLabel>
          </TreeItem>
        );
      })}
    </Tree>
  );
}
