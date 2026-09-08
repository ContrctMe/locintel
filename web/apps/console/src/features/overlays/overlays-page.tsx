import { Button, buttonVariants, ConfirmButton, Field, FieldLabel, FormDialog, Frame, FrameFooter, FrameHeader, FramePanel, Input, Select, Textarea, toast, ToggleGroup, ToggleGroupItem } from '@locintel/ui';
import { Link } from '@tanstack/react-router';
import { FileUp, Layers, MapPin, Plus, RotateCcw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useScope } from '../../app/scope';
import { EmptyState } from '../../components/page';
import { FileDropzone } from '../../components/file-dropzone';
import { useApiMutation } from '../../lib/mutation';
import { can, useMe } from '../../session';
import { useHierarchy } from '../sites/hooks';
import { overlaysApi, type OverlayLayer } from './api';
import { useOverlays } from './hooks';

const KINDS = ['territory', 'zone', 'region', 'trade area'] as const;
const DEFAULT_FILL = '#7c6cf0';

const fillOf = (layer: OverlayLayer) =>
  (layer.style as { fill?: string } | null | undefined)?.fill ?? DEFAULT_FILL;

/**
 * The org's overlays (ADR 50 §3): territories, zones, regions - shapes the
 * org owns and edits, drawn under the sites on the map and filtered by scope
 * like everything else. The first version takes GeoJSON uploads of polygons;
 * drawing in place comes later over the same endpoint.
 */
export function OverlaysPage() {
  const { data: me } = useMe();
  const manage = can(me, 'overlays:manage');
  const [tab, setTab] = useState<'active' | 'trash'>('active');
  const query = useOverlays(true, tab === 'trash');
  // the Scope node narrows the list to layers anchored under it; a layer
  // anchored to the whole org covers every scope, so it always shows
  const scope = useScope();
  const { data: hierarchy } = useHierarchy();
  const pathOf = useMemo(() => new Map((hierarchy?.nodes ?? []).map((n) => [n.id, n.path])), [hierarchy]);
  const scopePath = scope.nodeId ? pathOf.get(scope.nodeId) : undefined;
  const underScope = (layer: OverlayLayer) => {
    if (!scopePath) return true;
    if (!layer.nodeId) return true;
    const anchor = pathOf.get(layer.nodeId);
    return anchor !== undefined && (anchor === scopePath || anchor.startsWith(`${scopePath}.`));
  };
  const layers = (query.data?.layers ?? []).filter(underScope);

  const remove = useApiMutation({
    mutationFn: (id: string) => overlaysApi.remove(id),
    invalidate: [['overlays']],
    success: 'Layer moved to the trash',
  });
  const restore = useApiMutation({
    mutationFn: (id: string) => overlaysApi.restore(id),
    invalidate: [['overlays']],
    success: 'Layer restored',
  });

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Overlays</h1>
          <p className="text-sm text-muted-foreground">
            Your own shapes on the map, drawn under the sites and filtered by scope.
          </p>
        </div>
        <div className="flex items-center gap-3">
          <Link to="/sites" className={buttonVariants({ variant: 'outline' })}>
            <MapPin className="size-4" aria-hidden />
            See the map
          </Link>
          {manage && <NewLayerDialog />}
        </div>
      </div>

      <Frame>
        <FramePanel>
          <FrameHeader className="flex-row items-center gap-2">
            <ToggleGroup
              aria-label="Layers"
              variant="outline"
              size="sm"
              spacing={0}
              value={[tab]}
              onValueChange={(next) => {
                const key = next[0];
                if (key === 'active' || key === 'trash') setTab(key);
              }}
            >
              <ToggleGroupItem value="active">Active</ToggleGroupItem>
              <ToggleGroupItem value="trash">Trash</ToggleGroupItem>
            </ToggleGroup>
          </FrameHeader>

          {query.isPending && (
            <p role="status" className="px-4 py-6 text-sm text-muted-foreground">
              Loading layers…
            </p>
          )}
          {query.isError && (
            <div role="alert" className="px-4 py-6 text-sm">
              Could not load layers.{' '}
              <Button variant="outline" size="sm" onClick={() => void query.refetch()}>
                Retry
              </Button>
            </div>
          )}
          {query.data && layers.length === 0 && (
            <EmptyState
              icon={Layers}
              title={tab === 'trash' ? 'The trash is empty' : scopePath ? 'No overlay layers under this scope' : 'No overlay layers yet'}
              description={
                tab === 'trash'
                  ? 'Deleted layers wait here until you restore them.'
                  : manage
                    ? 'Create a layer, then upload its shapes as GeoJSON.'
                    : 'Nothing in your scope has been drawn yet.'
              }
            />
          )}
          {layers.length > 0 && (
            <ul className="divide-y">
              {layers.map((layer) => {
                const count = layer.featureCount;
                return (
                  <li key={layer.id} className="flex flex-wrap items-center gap-3 px-4 py-3">
                    <span
                      className="size-4 shrink-0 rounded-sm border"
                      style={{ background: fillOf(layer) }}
                      aria-hidden
                    />
                    <div className="min-w-0 flex-1">
                      <div className="truncate text-sm font-medium">{layer.name}</div>
                      <div className="text-xs text-muted-foreground">
                        {layer.kind} · {count === 0 ? 'no shapes yet' : `${count} shape${count === 1 ? '' : 's'}`}
                        {' · '}updated {new Date(layer.updatedAt).toLocaleDateString()}
                      </div>
                    </div>
                    {manage && tab === 'active' && (
                      <div className="flex items-center gap-1">
                        <UploadShapesDialog layer={layer} />
                        <ConfirmButton
                          size="sm"
                          confirmLabel="Move to trash?"
                          onConfirm={() => remove.mutate(layer.id)}
                          disabled={remove.isPending}
                        >
                          Delete
                        </ConfirmButton>
                      </div>
                    )}
                    {manage && tab === 'trash' && (
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={restore.isPending}
                        onClick={() => restore.mutate(layer.id)}
                      >
                        <RotateCcw className="size-4" aria-hidden />
                        Restore
                      </Button>
                    )}
                  </li>
                );
              })}
            </ul>
          )}

          <FrameFooter>
            <span className="text-sm text-muted-foreground" role="status">
              {query.data ? `${layers.length} ${tab === 'trash' ? 'in the trash' : 'in scope'}` : ''}
            </span>
          </FrameFooter>
        </FramePanel>
      </Frame>
    </div>
  );
}

function NewLayerDialog() {
  const { data: hierarchy } = useHierarchy();
  const scope = useScope();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [kind, setKind] = useState<string>(KINDS[0]);
  const [fill, setFill] = useState(DEFAULT_FILL);
  // a layer made under a scope anchors there unless told otherwise
  const [nodeId, setNodeId] = useState(scope.nodeId ?? '');

  const [shapes, setShapes] = useState<{ fileName: string; geoJson: unknown } | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  // one dialog (flow review, 2026-09): the layer and, when a file is dropped
  // in, its shapes - two calls behind one button
  const create = useApiMutation({
    mutationFn: async () => {
      const layer = await overlaysApi.create({
        name,
        kind,
        style: { fill, opacity: 0.2 },
        nodeId: nodeId || null,
      });
      if (!shapes) return { count: 0 };
      return overlaysApi.replaceFeatures(layer.id, shapes.geoJson);
    },
    invalidate: [['overlays']],
    onSuccess: (result) => {
      const n = result.count;
      toast.success(shapes ? `Layer created with ${n} shape${n === 1 ? '' : 's'}` : 'Layer created - upload its shapes next');
      setName('');
      setShapes(null);
      setOpen(false);
    },
  });
  const pick = async (file: File | undefined) => {
    if (!file) return;
    setProblem(null);
    try {
      setShapes({ fileName: file.name, geoJson: JSON.parse(await file.text()) as unknown });
    } catch {
      setShapes(null);
      setProblem(`${file.name} is not valid GeoJSON.`);
    }
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={setOpen}
      trigger={
        <Button>
          <Plus className="size-4" aria-hidden />
          New layer
        </Button>
      }
      title="New overlay layer"
      description="A layer holds shapes of one kind. Anchor it to a hierarchy node and only that subtree's scope sees it."
    >
      <div className="space-y-3">
        <Field>
          <FieldLabel htmlFor="overlay-name">Name</FieldLabel>
          <Input id="overlay-name" value={name} placeholder="Downtown zones" onChange={(e) => setName(e.target.value)} />
        </Field>
        <FileDropzone
          accept=".geojson,.json,application/geo+json,application/json"
          onFile={(file) => void pick(file)}
          label={shapes ? `Ready: ${shapes.fileName}` : 'Drop the shapes here as GeoJSON, or add them later'}
          buttonLabel="Choose GeoJSON…"
          error={problem ?? undefined}
        />
        <div className="grid grid-cols-[1fr_auto] gap-3">
          <Field>
            <FieldLabel htmlFor="overlay-kind">Kind</FieldLabel>
            <Select id="overlay-kind" value={kind} onChange={(e) => setKind(e.target.value)}>
              {KINDS.map((k) => (
                <option key={k} value={k}>
                  {k}
                </option>
              ))}
            </Select>
          </Field>
          <Field>
            <FieldLabel htmlFor="overlay-fill">Color</FieldLabel>
            <Input
              id="overlay-fill"
              type="color"
              className="h-9 w-14 p-1"
              value={fill}
              onChange={(e) => setFill(e.target.value)}
            />
          </Field>
        </div>
        <Field>
          <FieldLabel htmlFor="overlay-node">Anchor</FieldLabel>
          <Select id="overlay-node" value={nodeId} onChange={(e) => setNodeId(e.target.value)}>
            <option value="">Whole organization</option>
            {hierarchy?.nodes.map((n) => (
              <option key={n.id} value={n.id}>
                {' '.repeat(n.depth * 2)}
                {n.name}
              </option>
            ))}
          </Select>
        </Field>
        <Button className="w-full" disabled={!name.trim() || create.isPending} onClick={() => create.mutate()}>
          {shapes ? 'Create layer and upload shapes' : 'Create layer'}
        </Button>
      </div>
    </FormDialog>
  );
}

function UploadShapesDialog({ layer }: { layer: OverlayLayer }) {
  // the honest verb: a layer with nothing in it is uploaded to, not replaced
  const verb = layer.featureCount === 0 ? 'Upload shapes' : 'Replace shapes';
  const [open, setOpen] = useState(false);
  const [text, setText] = useState('');
  const [fileName, setFileName] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const upload = useApiMutation({
    mutationFn: (geoJson: unknown) => overlaysApi.replaceFeatures(layer.id, geoJson),
    invalidate: [['overlays']],
    onSuccess: (result) => {
      setText('');
      setFileName(null);
      setOpen(false);
      const n = result.count;
      // the success line carries the count, which the generic option cannot
      toast.success(`${n} shape${n === 1 ? '' : 's'} uploaded`);
    },
  });

  const submit = () => {
    setProblem(null);
    let parsed: unknown;
    try {
      parsed = JSON.parse(text);
    } catch {
      setProblem('That is not valid JSON.');
      return;
    }
    upload.mutate(parsed);
  };

  const pick = async (file: File | undefined) => {
    if (!file) return;
    setFileName(file.name);
    setText(await file.text());
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setProblem(null);
      }}
      trigger={
        <Button variant="outline" size="sm">
          <FileUp className="size-4" aria-hidden />
          {verb}
        </Button>
      }
      title={`Shapes for ${layer.name}`}
      description={
        layer.featureCount === 0
          ? 'A GeoJSON FeatureCollection of polygons; feature properties ride along to the map.'
          : 'A GeoJSON FeatureCollection of polygons. The upload replaces every shape the layer has; feature properties ride along to the map.'
      }
    >
      <div className="space-y-3">
        <FileDropzone
          accept=".geojson,.json,application/geo+json,application/json"
          onFile={(file) => void pick(file)}
          label={fileName ? `Ready: ${fileName}` : 'Drop a GeoJSON file here, or choose one'}
          buttonLabel="Choose GeoJSON…"
        />
        <Field>
          <FieldLabel htmlFor="overlay-geojson">Or paste it</FieldLabel>
          <Textarea
            id="overlay-geojson"
            rows={6}
            className="font-mono text-xs"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder='{"type":"FeatureCollection","features":[…]}'
          />
        </Field>
        {problem && (
          <p role="alert" className="text-sm text-destructive">
            {problem}
          </p>
        )}
        <Button className="w-full" disabled={!text.trim() || upload.isPending} onClick={submit}>
          {upload.isPending ? 'Uploading…' : verb}
        </Button>
      </div>
    </FormDialog>
  );
}
