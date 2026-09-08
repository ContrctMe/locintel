import { api } from '@locintel/api';
import { Button } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { fmtDateTime } from '../lib/format';



/**
 * The prosecution package as a print-ready page: fetching it IS the export
 * (a custody event on every file), and the browser's print-to-PDF is the
 * PDF. Plain document styling; no console chrome when printed.
 */
export function CasePackagePage() {
  const { caseId } = useParams({ strict: false }) as { caseId: string };
  const { data: pkg, isError } = useQuery({
    queryKey: ['cases', 'package', caseId],
    queryFn: ({ signal }) => api.get("/api/cases/{id}/package", { signal, path: { id: caseId } }),
    staleTime: Infinity,
  });
  if (isError) return <p className="text-sm text-destructive">The package could not be exported (managers only).</p>;
  if (!pkg) return <p className="text-sm text-muted-foreground">Preparing the package…</p>;
  const c = pkg.case;
  return (
    <div className="max-w-3xl space-y-6 print:max-w-none">
      <style>{`@media print { nav, aside, header, .no-print { display: none !important; } main { padding: 0 !important; } }`}</style>
      <div className="no-print flex items-center justify-between">
        <Link to="/cases/$caseId" params={{ caseId }} className="text-sm text-muted-foreground hover:underline">← Case</Link>
        <Button onClick={() => window.print()}>Print / save as PDF</Button>
      </div>
      <header className="border-b pb-4">
        <div className="text-xs uppercase text-muted-foreground">Case package · exported {fmtDateTime(pkg.exportedAt)} by {pkg.exportedByLabel ?? '—'}</div>
        <h1 className="mt-1 text-2xl font-semibold">{c.title}</h1>
        <p className="text-sm text-muted-foreground">
          {c.status} · {c.priority} priority · lead {c.lead ?? '—'} · opened {fmtDateTime(c.createdAt)}
          {c.disposition && ` · ${c.disposition}${c.closureNote ? `: ${c.closureNote}` : ''}`}
          {c.legalHold && ' · UNDER LEGAL HOLD'}
        </p>
        {c.summary && <p className="mt-3 whitespace-pre-wrap text-sm">{c.summary}</p>}
      </header>
      <Section title="Incidents">
        {c.incidents.map((i) => <li key={i.incidentId}>{i.occurredAt ? fmtDateTime(i.occurredAt) : '—'} · {i.category} · {i.title} ({i.status})</li>)}
      </Section>
      <Section title="Persons and vehicles">
        {c.entities.map((e, idx) => <li key={idx}>{e.restricted ? 'Restricted record (need-to-know)' : `${e.kind}: ${e.displayName} (${e.status})${e.note ? ` - ${e.note}` : ''}`}</li>)}
      </Section>
      <Section title="Evidence">
        {c.evidence.map((e, idx) => <li key={idx}>{e.fileName ?? 'file'}{e.label ? ` - ${e.label}` : ''} · added {fmtDateTime(e.addedAt)} by {e.addedByLabel ?? '—'}</li>)}
      </Section>
      <Section title="Chain of custody">
        {pkg.custody.map((ev, idx) => <li key={idx}>{fmtDateTime(ev.at)} · {ev.action} · {ev.actor ?? ev.actorTier}{ev.detail ? ` · ${ev.detail}` : ''} · file {ev.fileId.slice(0, 8)}</li>)}
      </Section>
      <Section title="Investigator notes">
        {c.notes.map((n, idx) => <li key={idx}><span className="text-muted-foreground">{fmtDateTime(n.createdAt)} · {n.author ?? '—'}:</span> {n.body}</li>)}
      </Section>
      <Section title="Tasks">
        {c.tasks.map((t, idx) => <li key={idx}>{t.doneAt ? '☑' : '☐'} {t.title}{t.assignee ? ` (${t.assignee})` : ''}</li>)}
      </Section>
      <Section title="Case members">
        {c.members.map((m, idx) => <li key={idx}>{m.user ?? '—'} · {m.role}</li>)}
      </Section>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode[] }) {
  return (
    <section>
      <h2 className="text-base font-semibold">{title}</h2>
      {children.length === 0 ? <p className="text-sm text-muted-foreground">None.</p> : <ul className="mt-1 list-disc space-y-1 pl-5 text-sm">{children}</ul>}
    </section>
  );
}
