import {
  Button,
  Cascader,
  CascaderBreadcrumb,
  CascaderContent,
  CascaderEmpty,
  CascaderInput,
  CascaderItems,
  CascaderList,
  CascaderNav,
  CascaderPanel,
  CascaderStatus,
  CascaderTrigger,
  CascaderValue,
  type CascaderNode,
} from '@locintel/ui';
import { useMemo } from 'react';

export type PickableNode = { id: string; name: string; parentId?: string | null; depth: number | string };

/**
 * The hierarchy node picker: the ReUI Cascader over the org's tree, so an
 * org of two thousand markets is a drill from region to market (with
 * search) rather than a select of two thousand options. Every node is
 * committable - a site can sit on a region as well as a market.
 */
export function NodePicker({
  id,
  nodes,
  value,
  onChange,
  placeholder = 'Choose a node…',
}: {
  id?: string;
  nodes: readonly PickableNode[];
  value: string;
  onChange: (nodeId: string) => void;
  placeholder?: string;
}) {
  // flat adjacency in: the cascader indexes it in one pass
  const items = useMemo<CascaderNode<PickableNode>[]>(
    () => nodes.map((n) => ({ value: n.id, label: n.name, data: n })),
    [nodes],
  );
  const parentOf = useMemo(() => new Map(nodes.map((n) => [n.id, n.parentId ?? null])), [nodes]);
  return (
    <Cascader
      items={items}
      getParent={(node) => parentOf.get(node.value) ?? null}
      selectable="any"
      value={value || undefined}
      onValueChange={(next) => onChange(next ?? '')}
    >
      <CascaderTrigger render={<Button id={id} variant="outline" className="w-full justify-between" />}>
        <CascaderValue placeholder={placeholder} />
      </CascaderTrigger>
      <CascaderContent className="w-(--anchor-width) min-w-80">
        <CascaderPanel>
          <CascaderNav>
            <CascaderInput />
          </CascaderNav>
          <CascaderBreadcrumb />
          <CascaderEmpty />
          <CascaderList>
            <CascaderItems />
          </CascaderList>
          <CascaderStatus />
        </CascaderPanel>
      </CascaderContent>
    </Cascader>
  );
}
