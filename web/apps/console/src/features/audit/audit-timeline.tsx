import type { components } from '@locintel/api';
import {
  Badge,
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
  Frame,
  FrameHeader,
  FramePanel,
  Timeline,
  TimelineContent,
  TimelineHeader,
  TimelineIndicator,
  TimelineItem,
  TimelineSeparator,
  TimelineTitle,
} from '@locintel/ui';
import { Activity, ChevronRight, FileDiff, ScrollText, ShieldCheck } from 'lucide-react';
import { useState, type ComponentType } from 'react';
import { fmtDateTime } from '../../lib/format';

export type AuditRow = components['schemas']['AuditRowResponse'];
export type AuditKind = 'events' | 'changes' | 'authz' | 'access';

const ICONS: Record<AuditKind, ComponentType<{ className?: string }>> = {
  events: Activity,
  changes: FileDiff,
  authz: ShieldCheck,
  access: ScrollText,
};

/** The headline of one row, per kind: what happened, in one line. */
export function auditHeadline(kind: AuditKind, row: AuditRow): string {
  switch (kind) {
    case 'events':
      return String(row.eventName ?? 'event');
    case 'changes':
      return `${String(row.operation)} ${String(row.schemaName)}.${String(row.tableName)}`;
    case 'authz':
      return `${String(row.action)} → ${String(row.outcome)}`;
    case 'access':
      return `${String(row.method)} ${String(row.path)} → ${String(row.statusCode)}`;
  }
}

/** A tone for the badge beside the headline, when the row carries an outcome. */
function auditTone(kind: AuditKind, row: AuditRow): { label: string; variant: 'success-light' | 'destructive-light' | 'warning-light' | 'secondary' } | null {
  if (kind === 'authz')
    return String(row.outcome) === 'Allowed'
      ? { label: 'allowed', variant: 'success-light' }
      : { label: String(row.outcome ?? 'denied').toLowerCase(), variant: 'destructive-light' };
  if (kind === 'access') {
    const code = row.statusCode ?? 0;
    if (code >= 500) return { label: String(code), variant: 'destructive-light' };
    if (code >= 400) return { label: String(code), variant: 'warning-light' };
    return { label: String(code), variant: 'success-light' };
  }
  if (kind === 'changes') return { label: String(row.operation ?? '').toLowerCase(), variant: 'secondary' };
  return null;
}

/** Rows grouped by the calendar day they happened on, newest day first. */
export function groupAuditByDay(rows: AuditRow[]): { day: string; rows: AuditRow[] }[] {
  const groups = new Map<string, AuditRow[]>();
  for (const row of rows) {
    const day = new Date(row.occurredAt).toLocaleDateString(undefined, {
      weekday: 'long',
      month: 'long',
      day: 'numeric',
    });
    groups.set(day, [...(groups.get(day) ?? []), row]);
  }
  return [...groups].map(([day, rows]) => ({ day, rows }));
}

function EventRow({ kind, row, step, isLast }: { kind: AuditKind; row: AuditRow; step: number; isLast: boolean }) {
  const [open, setOpen] = useState(false);
  const Icon = ICONS[kind];
  const tone = auditTone(kind, row);
  const actor = row.actorLabel ?? row.actorTier;
  return (
    <TimelineItem step={step} className={`ms-10 ${isLast ? 'pb-0' : 'pb-5'}`}>
      <TimelineHeader className="flex min-w-0 items-center justify-between gap-2.5">
        <TimelineSeparator className="bg-border! group-data-[orientation=vertical]/timeline:-left-7 group-data-[orientation=vertical]/timeline:h-[calc(100%-1.5rem-0.5rem)] group-data-[orientation=vertical]/timeline:translate-y-7" />
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <TimelineTitle className="truncate font-mono text-sm font-semibold">{auditHeadline(kind, row)}</TimelineTitle>
          {tone && (
            <Badge variant={tone.variant} size="sm">
              {tone.label}
            </Badge>
          )}
          <span className="text-xs text-muted-foreground">{fmtDateTime(row.occurredAt)}</span>
        </div>
        <TimelineIndicator className="flex size-6 items-center justify-center border border-border bg-background text-muted-foreground shadow-xs group-data-[orientation=vertical]/timeline:-left-7 [&_svg]:size-3.5">
          <Icon aria-hidden />
        </TimelineIndicator>
      </TimelineHeader>
      <TimelineContent className="mt-2">
        <Frame stacked dense spacing="sm">
          <Collapsible open={open} onOpenChange={setOpen} className="group/collapsible">
            <CollapsibleTrigger type="button" className="flex w-full" aria-label={`Toggle details of ${auditHeadline(kind, row)}`}>
              <FrameHeader className="flex grow flex-row items-center justify-between gap-2">
                <span className="min-w-0 truncate text-sm font-medium text-muted-foreground">{actor}</span>
                <ChevronRight
                  className="size-4 shrink-0 text-muted-foreground transition-transform duration-200 group-data-open/collapsible:rotate-90"
                  aria-hidden
                />
              </FrameHeader>
            </CollapsibleTrigger>
            <CollapsibleContent>
              <FramePanel>
                <pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all font-mono text-xs">
                  {JSON.stringify(row, null, 2)}
                </pre>
              </FramePanel>
            </CollapsibleContent>
          </Collapsible>
        </Frame>
      </TimelineContent>
    </TimelineItem>
  );
}

/**
 * The audit log as a day-grouped timeline (the ReUI users solution's
 * audit page): each row is a headline with its actor, and opens to the
 * full record. Kinds share the shape; only the headline differs.
 */
export function AuditTimeline({ kind, rows }: { kind: AuditKind; rows: AuditRow[] }) {
  const days = groupAuditByDay(rows);
  let step = 0;
  return (
    <div className="space-y-6">
      {days.map((group) => (
        <section key={group.day} aria-label={group.day} className="space-y-3">
          <h3 className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{group.day}</h3>
          <Timeline orientation="vertical" className="gap-0">
            {group.rows.map((row, i) => (
              <EventRow key={row.id} kind={kind} row={row} step={++step} isLast={i === group.rows.length - 1} />
            ))}
          </Timeline>
        </section>
      ))}
    </div>
  );
}
