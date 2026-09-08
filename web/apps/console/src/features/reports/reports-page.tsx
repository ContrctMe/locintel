import { Button } from '@locintel/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { useEffect, useRef } from 'react';
import { PageHeader, Panel } from '../../components/page';
import { fmtDateTime } from '../../lib/format';
import { useApiMutation } from '../../lib/mutation';
import { can, useMe } from '../../session';
import { reportsApi, type ReportArtifact, type ReportJob, type ReportJobState } from './api';

type Quota = Awaited<ReturnType<typeof reportsApi.quota>>;

const active = (state: ReportJobState) => state === 'Queued' || state === 'Running';

/** A generated PDF is only downloadable once Storage has published it as a file. */
const publishing = (artifact: ReportArtifact) =>
  artifact.contentType === 'application/pdf' && !artifact.fileId;

/**
 * How often to re-read a run, or false to stop. Backs off as the run ages so a
 * long batch does not poll like a short one, and always stops: a run that ends
 * with an unpublished PDF used to poll every second forever.
 */
export function pollInterval(job: ReportJob | undefined, now = Date.now()): number | false {
  if (!job) return false;
  const age = now - new Date(job.createdAt).getTime();
  const waiting = active(job.state) || (job.artifacts.some(publishing) && age < 300_000);
  if (!waiting) return false;
  return age < 10_000 ? 1000 : age < 60_000 ? 2000 : 5000;
}

export function ReportRunPage() {
  // The route supplies runId; render nothing rather than assert it.
  const { runId } = useParams({ strict: false });
  return runId ? <ReportsPage runId={runId} /> : null;
}

export function ReportsPage({ runId }: { runId?: string }) {
  const { data: me, isPending } = useMe();
  if (isPending) return <p role="status">Loading reports…</p>;
  if (!can(me, 'files:read'))
    return <p role="alert">You do not have permission to read report files.</p>;
  return <Reports runId={runId} />;
}

function Reports({ runId }: { runId?: string }) {
  const { data: me } = useMe();
  const generate = can(me, 'reports:generate');
  const quota = useQuery({
    queryKey: ['reports', 'quota'],
    queryFn: ({ signal }) => reportsApi.quota(signal),
    enabled: generate,
  });
  const list = useQuery({
    queryKey: ['reports', 'list'],
    queryFn: ({ signal }) => reportsApi.list(signal),
    // On a run page the run is the only poller and invalidates this itself.
    refetchInterval: runId
      ? false
      : (query) => (query.state.data?.some((x) => active(x.state)) ? 2000 : false),
  });

  const allowance = generate && (
    <Panel title="Organization report allowance">
      {quota.isPending && <p role="status">Loading report allowance…</p>}
      {quota.error && (
        <p role="alert">
          Report allowance could not load.{' '}
          <Button onClick={() => void quota.refetch()}>Reload allowance</Button>
        </p>
      )}
      {quota.data && (
        <>
          <p>
            {quota.data.enabled
              ? `${quota.data.remaining} of ${quota.data.limit} PDFs available`
              : 'Report generation is not included in your organization’s plan.'}
          </p>
          <p className="text-sm">
            {quota.data.consumed} generated · {quota.data.reserved} reserved · UTC month starting{' '}
            {quota.data.periodMonth}
          </p>
          <p className="text-sm text-muted-foreground">
            A single or aggregate PDF uses one report. Bulk generation uses one per site. ZIPs and
            downloads do not use additional reports. Failed or canceled PDFs release their
            reservations.
          </p>
        </>
      )}
    </Panel>
  );

  const runs = (
    <Panel
      title="Report runs"
      description="Latest 50 runs. Each run keeps its selection, progress and results together."
    >
      {list.isPending && <p role="status">Loading runs…</p>}
      {list.error && (
        <p role="alert">
          {list.error.message} <Button onClick={() => void list.refetch()}>Try again</Button>
        </p>
      )}
      {list.data?.length === 0 && <p>No reports requested yet.</p>}
      <ul className="space-y-2">
        {list.data?.map((report) => (
          <li key={report.id}>
            <Link
              to="/reports/$runId"
              params={{ runId: report.id }}
              className="block rounded border p-3 hover:underline"
              aria-current={runId === report.id ? 'page' : undefined}
            >
              {report.sites.length === 1
                ? report.sites[0]!.name
                : `${report.sites.length || report.items.length} sites`}{' '}
              · {report.reportType} · {fmtDateTime(report.createdAt)} · {report.state}
            </Link>
          </li>
        ))}
      </ul>
    </Panel>
  );

  // A deep link opens on its run, not on the allowance panel above it.
  if (runId)
    return (
      <div className="space-y-6">
        <ReportDetails key={runId} id={runId} quota={quota.data} />
        {allowance}
        {runs}
      </div>
    );

  return (
    <div className="space-y-6">
      <PageHeader
        title="Reports"
        description="Review report runs, progress, results and downloads. Start a new run by selecting sites in the site library."
      />
      {allowance}
      <Link to="/sites" className="underline">
        Choose sites to generate reports
      </Link>
      {runs}
    </div>
  );
}

function ReportDetails({ id, quota }: { id: string; quota?: Quota }) {
  const { data: me } = useMe();
  const heading = useRef<HTMLDivElement>(null);
  const report = useQuery({
    queryKey: ['reports', id],
    queryFn: ({ signal }) => reportsApi.get(id, signal),
    refetchInterval: (query) => pollInterval(query.state.data),
  });
  const job = report.data;

  // Arriving from the dialog, focus lands on the run rather than the document.
  useEffect(() => heading.current?.focus(), [id]);
  // The run is the only poller here, so it refreshes what its own progress
  // makes stale instead of the list and allowance polling on their own.
  const queryClient = useQueryClient();
  const state = job?.state;
  useEffect(() => {
    if (!state || active(state)) return;
    void queryClient.invalidateQueries({ queryKey: ['reports', 'list'] });
    void queryClient.invalidateQueries({ queryKey: ['reports', 'quota'] });
  }, [state, queryClient]);

  const cancel = useApiMutation({
    mutationFn: () => reportsApi.cancel(id),
    invalidate: [['reports']],
    success: 'Cancellation requested',
  });
  const retry = useApiMutation({
    mutationFn: () => reportsApi.retry(id),
    invalidate: [['reports']],
    success: 'Failed items queued for retry',
  });
  const download = useApiMutation({
    mutationFn: (artifact: ReportArtifact) =>
      artifact.fileId ? reportsApi.file(artifact.fileId) : reportsApi.bundle(id, artifact.id),
    onSuccess: (result) => {
      window.location.assign(result.url);
    },
  });

  const modify = !!job?.canModify && can(me, 'reports:generate');
  const expired = !!job?.expiresAt && new Date(job.expiresAt).getTime() <= Date.now();
  const failed = job?.items.filter((item) => item.state === 'Failed') ?? [];
  const bundle = job?.artifacts.find((x) => x.contentType === 'application/zip');
  const siteName = (siteId: string) =>
    job?.sites.find((s) => s.id === siteId)?.name ?? 'Site unavailable';

  const subject =
    job === undefined
      ? undefined
      : job.sites.length === 1
        ? job.sites[0]!.name
        : `${job.sites.length} sites`;

  return (
    <div className="space-y-4">
      {/* Focus lands here after the dialog navigates; the ring is deliberate. */}
      <div ref={heading} tabIndex={-1}>
        <PageHeader
          title="Report run"
          description={
            subject && `${subject} · ${job!.reportType} · ${fmtDateTime(job!.createdAt)}`
          }
        />
      </div>
      <Panel title="Progress and results">
      {report.isPending && <p role="status">Loading report…</p>}
      {report.error && <p role="alert">{report.error.message}</p>}
      {job && (
        <div className="space-y-3">
          <p role="status">
            {job.state} · {job.items.filter((i) => i.state === 'Succeeded').length} of{' '}
            {job.items.length} PDFs ready
          </p>
          {bundle && job.expiresAt && (
            <p className="text-sm">ZIP expires {fmtDateTime(job.expiresAt)}</p>
          )}
          {job.errorCode && <p role="alert">{job.errorCode}</p>}
          {modify && active(job.state) && (
            <Button disabled={cancel.isPending} onClick={() => cancel.mutate()}>
              Cancel report
            </Button>
          )}
          {modify && !expired && failed.length > 0 && (
            <>
              <Button
                disabled={retry.isPending || !quota?.enabled || quota.remaining < failed.length}
                onClick={() => retry.mutate()}
              >
                Retry failed items
              </Button>
              <p className="text-sm text-muted-foreground">
                Retry keeps successful PDFs and their original data. Submit a new request to
                regenerate everything.
              </p>
            </>
          )}
          <ul className="space-y-2">
            {job.items.map((item) => (
              <li key={item.id} className="rounded border p-3">
                <p>
                  {item.state} · Attempt {item.attempt}
                </p>
                <ul>
                  {item.siteIds.map((siteId) => (
                    <li key={siteId}>
                      <Link to="/sites/$siteId" params={{ siteId }} className="underline">
                        {siteName(siteId)}
                      </Link>
                    </li>
                  ))}
                </ul>
                {item.generatedAt && <p>Generated {fmtDateTime(item.generatedAt)}</p>}
                {item.errorCode && <p>{item.errorCode}</p>}
                {item.warnings.map((warning, i) => (
                  <p key={i}>{warning}</p>
                ))}
              </li>
            ))}
          </ul>
          {job.artifacts.map((artifact) => (
            <Button
              className="mr-2"
              key={artifact.id}
              variant="outline"
              disabled={
                download.isPending ||
                (artifact.contentType === 'application/zip' ? expired : publishing(artifact))
              }
              onClick={() => download.mutate(artifact)}
            >
              {publishing(artifact) ? 'Publishing' : 'Download'} {artifact.name}
            </Button>
          ))}
          {expired && bundle && (
            <p>The run’s ZIP has expired. Published PDFs remain in site files.</p>
          )}
          {(cancel.error || retry.error || download.error) && (
            <p role="alert">{(cancel.error || retry.error || download.error)?.message}</p>
          )}
        </div>
      )}
      </Panel>
    </div>
  );
}
