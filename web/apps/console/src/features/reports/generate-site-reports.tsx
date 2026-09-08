import { ApiError, apiPlanLimit } from '@locintel/api';
import {
  Button,
  Checkbox,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  Field,
  FieldLabel,
  FieldLegend,
  FieldSet,
  Input,
  Select,
  Textarea,
} from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useRef, useState } from 'react';
import { useApiMutation } from '../../lib/mutation';
import { can, useMe } from '../../session';
import { reportsApi, type ReportSubmission } from './api';
import { SitePhotoPicker } from './site-photo-picker';

const MAX_OVERLAYS = 10;

export function GenerateSiteReports({ sites }: { sites: Record<string, string> }) {
  const { data: me } = useMe();
  const [captured, setCaptured] = useState<Record<string, string>>();
  if (!can(me, 'reports:generate')) return null;
  return (
    <>
      <Button disabled={Object.keys(sites).length === 0} onClick={() => setCaptured({ ...sites })}>
        Generate report
      </Button>
      <ReportGenerationDialog sites={captured} onClose={() => setCaptured(undefined)} />
    </>
  );
}

export function ReportGenerationDialog({
  sites,
  onClose,
}: {
  sites?: Record<string, string>;
  onClose: () => void;
}) {
  const navigate = useNavigate();
  return (
    <Dialog
      open={!!sites}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Generate site reports</DialogTitle>
          <DialogDescription>
            Choose the report to generate for the sites you selected.
          </DialogDescription>
        </DialogHeader>
        {sites && (
          <ReportRequestForm
            selectedSites={sites}
            onCreated={(id) => {
              onClose();
              void navigate({ to: '/reports/$runId', params: { runId: id } });
            }}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

export function ReportRequestForm({
  selectedSites,
  onCreated,
}: {
  selectedSites: Record<string, string>;
  onCreated: (id: string) => void;
}) {
  const { data: me } = useMe();
  const canReadFiles = can(me, 'files:read');
  const canReadOverlays = can(me, 'overlays:read');
  const allowance = useQuery({
    queryKey: ['reports', 'quota'],
    queryFn: ({ signal }) => reportsApi.quota(signal),
  });
  const quota = allowance.data;
  const types = useQuery({
    queryKey: ['reports', 'types'],
    queryFn: ({ signal }) => reportsApi.types(signal),
  });
  const basemaps = useQuery({
    queryKey: ['reports', 'basemaps'],
    queryFn: ({ signal }) => reportsApi.basemaps(signal),
  });
  const overlays = useQuery({
    queryKey: ['reports', 'overlays'],
    queryFn: ({ signal }) => reportsApi.overlays(signal),
    enabled: canReadOverlays,
  });

  const siteIds = Object.keys(selectedSites);
  const [typeId, setTypeId] = useState('site');
  const type = types.data?.find((x) => x.id === typeId);
  const defaultMode = siteIds.length === 1 ? 'single' : 'bulk';
  const [mode, setMode] = useState<ReportSubmission['mode']>(defaultMode);
  const [provider, setProvider] = useState('');
  const [zoom, setZoom] = useState(14);
  const [overlayIds, setOverlayIds] = useState<string[]>([]);
  const [photos, setPhotos] = useState<Record<string, string[]>>({});
  const [customOptions, setCustomOptions] = useState('{}');
  const [validation, setValidation] = useState('');
  const lastRequest = useRef<{ body: string; key: string } | undefined>(undefined);

  const submit = useApiMutation({
    mutationFn: (body: ReportSubmission) => {
      const serialized = JSON.stringify(body);
      if (lastRequest.current?.body !== serialized)
        lastRequest.current = { body: serialized, key: crypto.randomUUID() };
      return reportsApi.submit(body, lastRequest.current.key);
    },
    invalidate: [
      ['reports', 'list'],
      ['reports', 'quota'],
    ],
    success: 'Report request accepted',
    onSuccess: (result) => {
      lastRequest.current = undefined;
      onCreated(result.id);
    },
  });

  const reference = typeId === 'site' || typeId === 'sites-summary';
  const effectiveMode = type?.aggregate ? 'aggregate' : mode;
  // One PDF per site for a bulk run; one for a single or aggregate report.
  const cost = effectiveMode === 'bulk' ? siteIds.length : 1;
  const mapsConfigured = (basemaps.data?.length ?? 0) > 0;
  // A plan limit is an upsell, not a failure: show the plan, not just the error.
  const planLimit =
    submit.error instanceof ApiError
      ? apiPlanLimit(submit.error.status, submit.error.body)
      : undefined;

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        setValidation('');
        try {
          const options: unknown = reference
            ? {
                ...(provider ? { mapProvider: provider } : {}),
                mapZoom: zoom,
                overlayIds,
                ...(type?.aggregate
                  ? {}
                  : {
                      photos: Object.fromEntries(
                        Object.entries(photos).filter(([id]) => id in selectedSites),
                      ),
                    }),
              }
            : JSON.parse(customOptions);
          if (!type) throw new Error('Choose an available report.');
          // Everything else this request must satisfy lives in validateSubmission.
          submit.mutate({
            reportType: typeId,
            mode: effectiveMode,
            selection: 'selected',
            siteIds,
            options,
          });
        } catch (error) {
          setValidation(error instanceof Error ? error.message : 'Check the report options.');
        }
      }}
    >
      <div className="flex flex-wrap gap-4">
        <Field>
          <FieldLabel htmlFor="report-type">Report type</FieldLabel>
          <Select
            id="report-type"
            className="w-56"
            value={typeId}
            onChange={(e) => {
              setTypeId(e.target.value);
              setMode(
                types.data?.find((t) => t.id === e.target.value)?.aggregate
                  ? 'aggregate'
                  : defaultMode,
              );
            }}
          >
            {types.data?.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </Select>
        </Field>
        {!type?.aggregate && (
          <Field>
            <FieldLabel htmlFor="report-output">Output</FieldLabel>
            <Select
              id="report-output"
              className="w-56"
              value={mode}
              onChange={(e) => setMode(e.target.value as ReportSubmission['mode'])}
            >
              <option value="single" disabled={siteIds.length !== 1}>
                One site PDF
              </option>
              <option value="bulk">Separate PDFs and ZIP</option>
            </Select>
          </Field>
        )}
      </div>

      <FieldSet>
        <FieldLegend>Selected sites ({siteIds.length})</FieldLegend>
        <ul>
          {Object.entries(selectedSites).map(([id, name]) => (
            <li key={id}>{name}</li>
          ))}
        </ul>
      </FieldSet>
      <p className="text-sm text-muted-foreground">
        This run covers the sites you selected. Oversized requests are rejected as a whole.
      </p>

      {allowance.isPending && <p role="status">Loading report allowance…</p>}
      {allowance.error && (
        <p role="alert">Report allowance could not load. Try reopening this dialog.</p>
      )}
      {quota &&
        (quota.enabled ? (
          <p>
            This run will use {cost} of {quota.remaining} PDFs available this UTC month.
          </p>
        ) : (
          <p>Report generation is not included in your organization’s plan.</p>
        ))}

      {reference ? (
        <>
          {mapsConfigured ? (
            <div className="flex flex-wrap gap-4">
              <Field>
                <FieldLabel htmlFor="report-basemap">Basemap</FieldLabel>
                <Select
                  id="report-basemap"
                  className="w-72"
                  value={provider}
                  onChange={(e) => setProvider(e.target.value)}
                >
                  <option value="">No basemap (report includes a warning)</option>
                  {basemaps.data?.map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.id}
                    </option>
                  ))}
                </Select>
              </Field>
              <Field>
                <FieldLabel htmlFor="report-zoom">Maximum map zoom</FieldLabel>
                <Input
                  id="report-zoom"
                  type="number"
                  min={0}
                  max={18}
                  required
                  value={zoom}
                  onChange={(e) => setZoom(Number(e.target.value))}
                />
              </Field>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              No basemap is configured for this organization, so reports include a map placeholder
              and a warning.
            </p>
          )}
          {mapsConfigured && provider && (
            <p className="text-sm">{basemaps.data?.find((b) => b.id === provider)?.attribution}</p>
          )}

          {canReadOverlays && (
            <FieldSet>
              <FieldLegend>Map overlays (up to {MAX_OVERLAYS})</FieldLegend>
              {overlays.data?.layers.length === 0 && (
                <p className="text-sm text-muted-foreground">
                  No overlays yet.{' '}
                  <Link to="/overlays" className="underline">
                    Add one
                  </Link>{' '}
                  to draw it on report maps.
                </p>
              )}
              {overlays.data?.layers.map((layer) => (
                <Field key={layer.id} orientation="horizontal">
                  <Checkbox
                    id={`overlay-${layer.id}`}
                    checked={overlayIds.includes(layer.id)}
                    disabled={
                      !overlayIds.includes(layer.id) && overlayIds.length >= MAX_OVERLAYS
                    }
                    onCheckedChange={() =>
                      setOverlayIds((previous) =>
                        previous.includes(layer.id)
                          ? previous.filter((id) => id !== layer.id)
                          : [...previous, layer.id],
                      )
                    }
                  />
                  <FieldLabel htmlFor={`overlay-${layer.id}`} className="font-normal">
                    {layer.name}
                  </FieldLabel>
                </Field>
              ))}
            </FieldSet>
          )}

          {!type?.aggregate &&
            canReadFiles &&
            Object.entries(selectedSites).map(([siteId, name]) => (
              <SitePhotoPicker
                key={siteId}
                siteId={siteId}
                siteName={name}
                chosen={photos[siteId] ?? []}
                onChange={(ids) => setPhotos((previous) => ({ ...previous, [siteId]: ids }))}
              />
            ))}
        </>
      ) : (
        <Field>
          <FieldLabel htmlFor="report-options">Report options</FieldLabel>
          <Textarea
            id="report-options"
            rows={4}
            value={customOptions}
            onChange={(e) => setCustomOptions(e.target.value)}
          />
        </Field>
      )}

      {(types.error || basemaps.error || overlays.error) && (
        <p role="alert">Some report choices could not load. Refresh the page to try again.</p>
      )}
      {validation && <p role="alert">{validation}</p>}
      {planLimit && (
        <div role="alert" className="space-y-1 rounded-md border p-3 text-sm">
          <p className="font-medium">
            {planLimit.limit === undefined
              ? 'Your plan does not include report generation.'
              : `Your plan allows ${planLimit.limit} PDFs a month and ${planLimit.current ?? planLimit.limit} are used.`}
          </p>
          <p>
            <Link to="/settings" className="underline">
              Review your plan under Billing
            </Link>{' '}
            to generate more this month.
          </p>
        </div>
      )}
      {submit.error && !planLimit && <p role="alert">{submit.error.message}</p>}

      <Button
        type="submit"
        disabled={
          submit.isPending ||
          !type ||
          !can(me, 'reports:generate') ||
          !quota?.enabled ||
          quota.remaining < cost
        }
      >
        {submit.isPending ? 'Submitting…' : 'Generate report'}
      </Button>
    </form>
  );
}
