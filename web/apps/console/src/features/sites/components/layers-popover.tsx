import { Button, Checkbox, Popover, PopoverContent, PopoverTrigger, RadioGroup, RadioGroupItem } from '@locintel/ui';
import { Layers } from 'lucide-react';
import type { useMapLayers } from './use-map-layers';

/** The map's Layers popover: basemap, data layer with its legend, and the overlays that are showing. */
export function LayersPopover({ layers }: { layers: ReturnType<typeof useMapLayers> }) {
  const {
    overlays,
    basemapChoices,
    knownChoice,
    basemapChoice,
    chooseBasemap,
    dataLayers,
    layerName,
    chooseLayer,
    dataLayer,
    overlayLayers,
    hiddenOverlays,
    toggleOverlay,
  } = layers;
  return (
    <Popover>
      <PopoverTrigger
        render={
          <Button variant="outline" size="sm" className="ml-auto">
            <Layers className="size-4" aria-hidden />
            Layers
            {overlays.length > 0 && (
              <span className="text-muted-foreground">{overlays.length}</span>
            )}
          </Button>
        }
      />
      <PopoverContent align="end" className="w-64">
        <p className="px-1 pb-1 text-xs font-medium text-muted-foreground">Basemap</p>
        <RadioGroup
          aria-label="Basemap"
          className="gap-0.5"
          value={knownChoice ? basemapChoice : 'auto'}
          onValueChange={(value) => chooseBasemap(String(value))}
        >
          {basemapChoices.map((c) => (
            <label
              key={c.id}
              className="flex cursor-pointer items-center gap-2 rounded-md px-1 py-1 text-sm hover:bg-muted"
            >
              <RadioGroupItem value={c.id} />
              <span className="truncate">{c.name}</span>
            </label>
          ))}
        </RadioGroup>
        {dataLayers.length > 1 && (
          <>
            <p className="px-1 pb-1 pt-1 text-xs font-medium text-muted-foreground">Data layer</p>
            <RadioGroup
              aria-label="Data layer"
              className="gap-0.5"
              value={layerName}
              onValueChange={(value) => chooseLayer(String(value))}
            >
              {dataLayers.map((l) => (
                <label
                  key={l.name}
                  className="flex cursor-pointer items-center gap-2 rounded-md px-1 py-1 text-sm hover:bg-muted"
                  title={l.description}
                >
                  <RadioGroupItem value={l.name} />
                  <span className="truncate">{l.title}</span>
                </label>
              ))}
            </RadioGroup>
          </>
        )}
        {dataLayer && dataLayer.statuses.length > 0 && (
          <ul className="flex flex-wrap gap-x-3 gap-y-1 px-1 text-xs text-muted-foreground" aria-label="Legend">
            {dataLayer.statuses.map((s) => (
              <li key={s.key} className="flex items-center gap-1.5">
                <span
                  className="size-2.5 rounded-full border border-background"
                  style={{ background: s.color }}
                  aria-hidden
                />
                {s.label}
              </li>
            ))}
          </ul>
        )}
        <p className="px-1 pt-1 text-xs font-medium text-muted-foreground">Overlays</p>
        {overlayLayers.length === 0 ? (
          <p className="px-1 text-sm text-muted-foreground">
            No overlay layers with shapes yet.
          </p>
        ) : (
          <ul className="flex flex-col gap-1">
            {overlayLayers.map((l) => (
              <li key={l.id}>
                <label className="flex cursor-pointer items-center gap-2 rounded-md px-1 py-1 text-sm hover:bg-muted">
                  <Checkbox
                    checked={!hiddenOverlays.has(l.id)}
                    onCheckedChange={() => toggleOverlay(l.id)}
                  />
                  <span
                    className="size-3 shrink-0 rounded-sm border"
                    style={{
                      background: (l.style as { fill?: string } | null)?.fill ?? '#7c6cf0',
                    }}
                    aria-hidden
                  />
                  <span className="truncate">{l.name}</span>
                  <span className="ml-auto text-xs text-muted-foreground">{l.kind}</span>
                </label>
              </li>
            ))}
          </ul>
        )}
      </PopoverContent>
    </Popover>
  );
}
