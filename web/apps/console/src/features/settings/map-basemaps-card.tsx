import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, ConfirmButton, Field, FieldLabel, Input, Item, ItemActions, ItemContent, ItemDescription, ItemTitle, Label } from '@locintel/ui';
import { Panel } from '../../components/page';
import { useApiMutation } from '../../lib/mutation';
import { settingsApi } from './api';

/**
 * The org's own basemaps (ADR 50 §3, the `map.basemaps` setting): raster
 * tile providers the map offers beside the open ones it ships with. A
 * provider key is written once and never shown again; the map receives it
 * in the tile URL, which is how providers key by referrer.
 */
export function MapBasemapsCard() {
  const [name, setName] = useState('');
  const [id, setId] = useState('');
  const [urlTemplate, setUrlTemplate] = useState('');
  const [attribution, setAttribution] = useState('');
  const [maxZoom, setMaxZoom] = useState('19');
  const [key, setKey] = useState('');

  const { data } = useQuery({
    queryKey: ['basemaps', 'settings'],
    queryFn: ({ signal }) => settingsApi.basemaps(signal),
  });
  const entries = data?.basemaps ?? [];
  // the whole list is the setting: a save carries every entry, keys kept server-side by id
  const keep = entries.map((e) => ({
    id: e.id,
    name: e.name,
    urlTemplate: e.urlTemplate,
    attribution: e.attribution,
    maxZoom: e.maxZoom,
  }));
  const save = useApiMutation({
    mutationFn: (basemaps: typeof keep & { key?: string | null }[]) =>
      settingsApi.saveBasemaps(basemaps),
    invalidate: [['basemaps']],
    success: 'Basemaps saved',
    onSuccess: () => {
      setName('');
      setId('');
      setUrlTemplate('');
      setAttribution('');
      setMaxZoom('19');
      setKey('');
    },
  });
  const needsKey = urlTemplate.includes('{key}');

  return (
    <Panel title="Map basemaps" bodyClassName="space-y-3">
        <p className="text-sm text-muted-foreground">
          Raster tile providers the map offers beside OpenStreetMap and the themed default. Put{' '}
          <code className="rounded bg-muted px-1">{'{key}'}</code> in the URL where the provider wants its
          key. Use a browser/public tile key: map viewers can see it in network requests.
          Restrict it to tile reads, your allowed origins, and a provider quota. Never enter a server secret.
        </p>
        {entries.map((e) => (
          <Item key={e.id} variant="outline" size="sm">
            <ItemContent>
              <ItemTitle>{e.name}</ItemTitle>
              <ItemDescription>
                {e.id} · zoom {String(e.maxZoom)}{e.hasKey && ' · keyed'}
                <span className="block truncate">{e.urlTemplate}</span>
              </ItemDescription>
            </ItemContent>
            <ItemActions>
              <ConfirmButton size="sm" variant="ghost" confirmLabel="Remove?" disabled={save.isPending}
              onConfirm={() => save.mutate(keep.filter((k) => k.id !== e.id))}>
              Remove
            </ConfirmButton>
            </ItemActions>
          </Item>
        ))}
        <div className="grid gap-2 sm:grid-cols-2">
          <Field>
            <FieldLabel htmlFor="basemap-name">Name</FieldLabel>
            <Input id="basemap-name" value={name} placeholder="Aerial"
              onChange={(e) => {
                setName(e.target.value);
                setId(e.target.value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, ''));
              }} />
          </Field>
          <Field>
            <FieldLabel htmlFor="basemap-id">Id</FieldLabel>
            <Input id="basemap-id" className="font-mono text-xs" value={id} onChange={(e) => setId(e.target.value)} />
          </Field>
          <div className="space-y-1 sm:col-span-2">
            <Label htmlFor="basemap-url">URL template</Label>
            <Input id="basemap-url" className="font-mono text-xs" value={urlTemplate}
              placeholder="https://tiles.example.com/{z}/{x}/{y}.png?key={key}"
              onChange={(e) => setUrlTemplate(e.target.value)} />
          </div>
          <Field>
            <FieldLabel htmlFor="basemap-attribution">Attribution</FieldLabel>
            <Input id="basemap-attribution" value={attribution} placeholder="© Example Maps"
              onChange={(e) => setAttribution(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="basemap-zoom">Max zoom</FieldLabel>
            <Input id="basemap-zoom" type="number" min={1} max={22} value={maxZoom}
              onChange={(e) => setMaxZoom(e.target.value)} />
          </Field>
          {needsKey && (
            <div className="space-y-1 sm:col-span-2">
              <Label htmlFor="basemap-key">Provider key</Label>
              <Input id="basemap-key" type="password" autoComplete="off" value={key}
                onChange={(e) => setKey(e.target.value)} />
            </div>
          )}
        </div>
        <Button size="sm" disabled={!name.trim() || !id || !urlTemplate.trim() || (needsKey && !key) || save.isPending}
          onClick={() =>
            save.mutate([
              ...keep,
              { id, name: name.trim(), urlTemplate: urlTemplate.trim(), attribution: attribution.trim(),
                maxZoom: Number(maxZoom) || 19, key: key || null },
            ])
          }>
          Add basemap
        </Button>
      </Panel>
  );
}
