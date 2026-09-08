import { Button, Field, FieldLabel, FormDialog, Input, TimeZoneSelect } from '@locintel/ui';
import { useNavigate } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useApiMutation } from '../../../lib/mutation';
import { NodePicker } from '../../hierarchy/node-picker';
import { sitesApi } from '../api';
import { useHierarchy } from '../hooks';

/** The zone this browser runs in, when the runtime knows it (the console's own default is a guess otherwise). */
function browserTimeZone(): string {
  try {
    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    return Intl.supportedValuesOf('timeZone').includes(zone) ? zone : 'Etc/UTC';
  } catch {
    return 'Etc/UTC';
  }
}

/**
 * One step to a site (flow review, 2026-09): name, where it sits, and the
 * clock it runs on, with the address and coordinates folded under a toggle
 * so a first site takes three fields and a mapped one takes one dialog.
 * Creating lands on the new site, where hours and closures live.
 */
export function NewSiteDialog() {
  const { data: hierarchy } = useHierarchy();
  const navigate = useNavigate();
  // a session change finishes pending writes with this tree unmounted: the
  // site is created, but the new session has nowhere to land on it
  const mounted = useRef(true);
  useEffect(() => () => { mounted.current = false; }, []);
  const [name, setName] = useState('');
  const [timeZone, setTimeZone] = useState(browserTimeZone);
  const [nodeId, setNodeId] = useState('');
  const [creating, setCreating] = useState(false);
  const [withAddress, setWithAddress] = useState(false);
  const [address, setAddress] = useState('');
  const [city, setCity] = useState('');
  const [postal, setPostal] = useState('');
  const [country, setCountry] = useState('');
  const [lat, setLat] = useState('');
  const [lng, setLng] = useState('');
  const coordinates =
    Number.isFinite(Number.parseFloat(lat)) && Number.isFinite(Number.parseFloat(lng))
      ? { latitude: Number.parseFloat(lat), longitude: Number.parseFloat(lng) }
      : {};
  const nodes = hierarchy?.nodes ?? [];

  const create = useApiMutation({
    mutationFn: () =>
      sitesApi.create({
        nodeId,
        name: name.trim(),
        timeZone,
        ...(withAddress
          ? {
              addressLine1: address.trim() || null,
              city: city.trim() || null,
              postalCode: postal.trim() || null,
              countryCode: country.trim() || null,
              ...coordinates,
            }
          : {}),
      }),
    invalidate: [['sites']],
    success: 'Site created',
    onSuccess: (site) => {
      setName('');
      setAddress('');
      setCity('');
      setPostal('');
      setCountry('');
      setLat('');
      setLng('');
      setCreating(false);
      if (mounted.current) void navigate({ to: '/sites/$siteId', params: { siteId: site.id } });
    },
  });

  return (
    <FormDialog
      open={creating}
      onOpenChange={setCreating}
      trigger={
        <Button>
          <Plus className="size-4" aria-hidden />
          New site
        </Button>
      }
      title="New site"
      description="A physical location on its own clock. Hours and closures come next, on the site itself."
    >
      <div className="space-y-3">
        <Field>
          <FieldLabel htmlFor="site-name">Name</FieldLabel>
          <Input id="site-name" value={name} placeholder="Pike Place" onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field>
          <FieldLabel htmlFor="site-node">Hierarchy node</FieldLabel>
          <NodePicker id="site-node" nodes={nodes} value={nodeId} onChange={setNodeId} />
        </Field>
        <Field>
          <FieldLabel htmlFor="site-tz">Time zone</FieldLabel>
          <TimeZoneSelect
            id="site-tz"
            value={timeZone}
            onChange={(e) => setTimeZone(e.target.value)}
          />
        </Field>
        {withAddress ? (
          <div className="space-y-3 border-t pt-3">
            <Field>
              <FieldLabel htmlFor="site-address">Address</FieldLabel>
              <Input id="site-address" value={address} onChange={(e) => setAddress(e.target.value)} />
            </Field>
            <div className="grid grid-cols-[1fr_auto_auto] gap-2">
              <Field>
                <FieldLabel htmlFor="site-city">City</FieldLabel>
                <Input id="site-city" value={city} onChange={(e) => setCity(e.target.value)} />
              </Field>
              <Field>
                <FieldLabel htmlFor="site-postal">Postal code</FieldLabel>
                <Input id="site-postal" className="w-28" value={postal} onChange={(e) => setPostal(e.target.value)} />
              </Field>
              <Field>
                <FieldLabel htmlFor="site-country">Country</FieldLabel>
                <Input id="site-country" className="w-16" value={country} placeholder="US" maxLength={2}
                  onChange={(e) => setCountry(e.target.value)} />
              </Field>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <Field>
                <FieldLabel htmlFor="site-lat">Latitude</FieldLabel>
                <Input id="site-lat" value={lat} placeholder="47.6097" inputMode="decimal"
                  onChange={(e) => setLat(e.target.value)} />
              </Field>
              <Field>
                <FieldLabel htmlFor="site-lng">Longitude</FieldLabel>
                <Input id="site-lng" value={lng} placeholder="-122.3422" inputMode="decimal"
                  onChange={(e) => setLng(e.target.value)} />
              </Field>
            </div>
            <p className="text-xs text-muted-foreground">Coordinates put this site on the map and the public locator.</p>
          </div>
        ) : (
          <Button type="button" variant="link" size="sm" className="h-auto px-0" onClick={() => setWithAddress(true)}>
            <Plus className="size-4" aria-hidden />
            Add address and coordinates
          </Button>
        )}
        <Button
          className="w-full"
          disabled={!name.trim() || !nodeId || create.isPending}
          onClick={() => create.mutate()}
        >
          Create site
        </Button>
      </div>
    </FormDialog>
  );
}
