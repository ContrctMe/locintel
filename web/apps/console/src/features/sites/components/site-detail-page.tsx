import type { components } from '@locintel/api';
import { Button, Checkbox, ConfirmButton, Field, FieldLabel, FormDialog, Input, Label, TimeZoneSelect } from '@locintel/ui';
import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';
import { useApiMutation } from '../../../lib/mutation';
import { can, useMe } from '../../../session';
import { StatusBadge } from '../../../shell';
import { sitesApi } from '../api';
import { useSite, useSiteAttributes, useRefreshSite } from '../hooks';
import { SiteHours } from './site-hours';
import { GenerateSiteReports } from '../../reports';
import { FileLibrary } from '../../files';

type UpdateSite = components['schemas']['UpdateSiteRequest'];

export function SiteDetailPage() {
  const { siteId } = useParams({ strict: false }) as { siteId: string };
  const { data: me } = useMe();
  const manage = can(me, 'sites:manage');

  const siteQuery = useSite(siteId);
  const site = siteQuery.data;

  const invalidate = useRefreshSite(siteId);

  const update = useApiMutation({
    mutationFn: (body: Partial<Omit<UpdateSite, 'version' | 'attributes'>> & {
      attributes?: Record<string, string | number | boolean | null>;
    }) =>
      sitesApi.update(
        siteId,
        {
          // the contract requires the three core fields; null means "unchanged"
          name: body.name ?? null,
          timeZone: body.timeZone ?? null,
          status: body.status ?? null,
          addressLine1: body.addressLine1,
          city: body.city,
          postalCode: body.postalCode,
          countryCode: body.countryCode,
          latitude: body.latitude,
          longitude: body.longitude,
          // an untyped JSON dictionary in the contract renders as an empty record
          attributes: body.attributes as UpdateSite['attributes'],
          // echo the version we loaded: a 409 means someone else saved first
          version: site?.version,
        },
      ),
    success: 'Site updated',
    onSuccess: invalidate,
    onError: invalidate, // a conflict means our copy is stale - reload it
  });
  const [editOpen, setEditOpen] = useState(false);
  const [editName, setEditName] = useState('');
  const [editZone, setEditZone] = useState('');
  const [editAddress, setEditAddress] = useState('');
  const [editCity, setEditCity] = useState('');
  const [editPostal, setEditPostal] = useState('');
  const [editCountry, setEditCountry] = useState('');
  const [editLat, setEditLat] = useState('');
  const [editLng, setEditLng] = useState('');
  const [editAttributes, setEditAttributes] = useState<Record<string, string>>({});
  const { data: definitions } = useSiteAttributes();
  if (siteQuery.isError)
    return <p className="text-sm text-destructive">Could not load this site.</p>;
  if (!site) return <p className="text-sm text-muted-foreground">Loading site…</p>;
  const closed = site.status === 'Closed';

  return (
    <div className="max-w-3xl space-y-6">
      <Link to="/sites" className="text-sm text-muted-foreground hover:underline">
        ← All sites
      </Link>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h1 className="text-xl font-semibold tracking-tight">{site.name}</h1>
          <StatusBadge status={site.status} />
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <GenerateSiteReports sites={{ [siteId]: site.name }} />
          {manage && (
            <div className="flex gap-2">
              <FormDialog
                open={editOpen}
                onOpenChange={setEditOpen}
                trigger={
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      setEditName(site.name);
                      setEditZone(site.timeZone);
                      setEditAddress(site.addressLine1 ?? '');
                      setEditCity(site.city ?? '');
                      setEditPostal(site.postalCode ?? '');
                      setEditCountry(site.countryCode ?? '');
                      setEditLat(site.latitude?.toString() ?? '');
                      setEditLng(site.longitude?.toString() ?? '');
                      setEditAttributes(
                        Object.fromEntries(
                          Object.entries(site.attributes ?? {}).map(([k, v]) => [k, String(v)]),
                        ),
                      );
                    }}
                  >
                    Edit
                  </Button>
                }
                title="Edit site"
                description="Changing the time zone re-anchors the hours projection."
              >
                <div className="space-y-3">
                  <Field>
                    <FieldLabel htmlFor="edit-site-name">Name</FieldLabel>
                    <Input id="edit-site-name" value={editName}
                      onChange={(e) => setEditName(e.target.value)} />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="edit-site-tz">Time zone</FieldLabel>
                    <TimeZoneSelect id="edit-site-tz" value={editZone}
                      onChange={(e) => setEditZone(e.target.value)} />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="edit-site-address">Street address</FieldLabel>
                    <Input id="edit-site-address" value={editAddress}
                      onChange={(e) => setEditAddress(e.target.value)} />
                  </Field>
                  <div className="grid grid-cols-2 gap-2">
                    <Field>
                      <FieldLabel htmlFor="edit-site-city">City</FieldLabel>
                      <Input id="edit-site-city" value={editCity}
                        onChange={(e) => setEditCity(e.target.value)} />
                    </Field>
                    <Field>
                      <FieldLabel htmlFor="edit-site-postal">Postal code</FieldLabel>
                      <Input id="edit-site-postal" value={editPostal}
                        onChange={(e) => setEditPostal(e.target.value)} />
                    </Field>
                  </div>
                  <Field>
                    <FieldLabel htmlFor="edit-site-country">Country code</FieldLabel>
                    <Input id="edit-site-country" value={editCountry} placeholder="US"
                      maxLength={2} onChange={(e) => setEditCountry(e.target.value)} />
                  </Field>
                  <div className="grid grid-cols-2 gap-2">
                    <Field>
                      <FieldLabel htmlFor="edit-site-lat">Latitude</FieldLabel>
                      <Input id="edit-site-lat" value={editLat} placeholder="42.3601"
                        onChange={(e) => setEditLat(e.target.value)} />
                    </Field>
                    <Field>
                      <FieldLabel htmlFor="edit-site-lng">Longitude</FieldLabel>
                      <Input id="edit-site-lng" value={editLng} placeholder="-71.0589"
                        onChange={(e) => setEditLng(e.target.value)} />
                    </Field>
                  </div>
                  <p className="text-xs text-muted-foreground">
                    Coordinates put this site on the public locator map.
                  </p>
                  {definitions?.map((d) => (
                    <div key={d.id} className="space-y-1">
                      <Label htmlFor={`attr-${d.key}`}>{d.label}</Label>
                      {d.type === 'Boolean' ? (
                        <Checkbox
                          id={`attr-${d.key}`}
                          checked={editAttributes[d.key] === 'true'}
                          onCheckedChange={(checked) =>
                            setEditAttributes({ ...editAttributes, [d.key]: String(checked === true) })
                          }
                        />
                      ) : (
                        <Input id={`attr-${d.key}`} value={editAttributes[d.key] ?? ''}
                          inputMode={d.type === 'Number' ? 'decimal' : undefined}
                          onChange={(e) =>
                            setEditAttributes({ ...editAttributes, [d.key]: e.target.value })
                          } />
                      )}
                    </div>
                  ))}
                  <Button className="w-full"
                    disabled={!editName.trim() || update.isPending}
                    onClick={() =>
                      update.mutate(
                        {
                          name: editName.trim(),
                          timeZone: editZone,
                          addressLine1: editAddress.trim(),
                          city: editCity.trim(),
                          postalCode: editPostal.trim(),
                          countryCode: editCountry.trim(),
                          ...(Number.isFinite(Number.parseFloat(editLat)) &&
                          Number.isFinite(Number.parseFloat(editLng))
                            ? {
                                latitude: Number.parseFloat(editLat),
                                longitude: Number.parseFloat(editLng),
                              }
                            : {}),
                          attributes: Object.fromEntries(
                            (definitions ?? []).map((d) => {
                              const raw = editAttributes[d.key] ?? '';
                              if (raw === '') return [d.key, null]; // cleared
                              if (d.type === 'Boolean') return [d.key, raw === 'true'];
                              if (d.type === 'Number') return [d.key, Number.parseFloat(raw)];
                              return [d.key, raw];
                            }),
                          ),
                        },
                        { onSuccess: () => setEditOpen(false) },
                      )
                    }>
                    Save
                  </Button>
                </div>
              </FormDialog>
              <ConfirmButton
                variant={closed ? 'default' : 'destructive'}
                size="sm"
                confirmLabel={closed ? 'Reopen?' : 'Close to the public?'}
                disabled={update.isPending}
                onConfirm={() => update.mutate({ status: closed ? 'Open' : 'Closed' })}
              >
                {closed ? 'Reopen site' : 'Close site'}
              </ConfirmButton>
            </div>
          )}
        </div>
      </div>
      <p className="text-sm text-muted-foreground">
        Time zone: {site.timeZone}
        {site.addressLine1 &&
          ` · ${[site.addressLine1, site.city, site.postalCode, site.countryCode]
            .filter(Boolean)
            .join(', ')}`}
      </p>

      {can(me, 'files:read') && <FileLibrary siteId={siteId} />}

      <SiteHours key={siteId} siteId={siteId} timeZone={site.timeZone} manage={manage} />
    </div>
  );
}
