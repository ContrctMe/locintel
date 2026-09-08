import {
  Filters,
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupInput,
  createFilterQuery,
  flattenFilterConditions,
  type FilterField,
  type FilterQuery,
} from '@locintel/ui';
import { CircleDot, Search, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { StatusBadge } from '../../../shell';
import { useDebounced } from '../../../lib/debounce';

export type SiteFilterValues = {
  /** Free text over name and city (the server's `q`). */
  q: string;
  /** Lifecycle statuses to keep (the server's `status`), empty for all. */
  statuses: string[];
};

const STATUS_OPTIONS = [
  { value: 'Open', label: 'Open' },
  { value: 'ComingSoon', label: 'Coming soon' },
  { value: 'TemporarilyClosed', label: 'Temporarily closed' },
  { value: 'Closed', label: 'Closed' },
];

const FIELDS: FilterField[] = [
  {
    id: 'status',
    label: 'Status',
    icon: <CircleDot className="size-3.5" aria-hidden />,
    type: 'select',
    searchable: false,
    options: STATUS_OPTIONS,
    renderValue: ({ values }) => {
      if (values.length === 0) return 'Select…';
      if (values.length > 1) return `${values.length} selected`;
      return <StatusBadge status={String(values[0])} />;
    },
  },
];

/** The statuses a filter tree means; an empty select means nothing. */
export function toStatuses(query: FilterQuery): string[] {
  const statuses = new Set<string>();
  for (const condition of flattenFilterConditions(query)) {
    if (condition.field !== 'status') continue;
    const chosen = condition.values.map((v) => String(v)).filter(Boolean);
    if (chosen.length === 0) continue;
    if (condition.negated || condition.operator === 'is_not' || condition.operator === 'is_none_of') {
      for (const s of STATUS_OPTIONS.map((o) => o.value)) if (!chosen.includes(s)) statuses.add(s);
    } else {
      for (const s of chosen) statuses.add(s);
    }
  }
  return [...statuses];
}

/**
 * The Sites filter bar: a search box for name or city (the one filter
 * everyone reaches for, so it is always visible), and the ReUI Filters
 * component for the rest, starting empty so the bar is just "Add filter"
 * until a chip is chosen. Both become the list query; the server filters,
 * so the map and the table read the same rows.
 */
export function SiteFilters({ onChange }: { onChange: (values: SiteFilterValues) => void }) {
  const [q, setQ] = useState('');
  const [query, setQuery] = useState<FilterQuery>(() => createFilterQuery());
  const fields = useMemo(() => FIELDS, []);
  // the search box reaches the server once typing pauses (ADR 51: a query
  // per keystroke is a scan per keystroke at fleet scale); chips are immediate
  const debouncedQ = useDebounced(q, 250);
  const latest = useRef({ onChange, query });
  latest.current = { onChange, query };
  useEffect(() => {
    latest.current.onChange({ q: debouncedQ.trim(), statuses: toStatuses(latest.current.query) });
  }, [debouncedQ]);
  const emit = (nextQ: string, nextQuery: FilterQuery) => onChange({ q: nextQ.trim(), statuses: toStatuses(nextQuery) });
  return (
    <div className="flex min-w-0 flex-wrap items-center gap-2">
      <InputGroup className="w-full sm:w-64">
        <InputGroupAddon align="inline-start">
          <Search aria-hidden />
        </InputGroupAddon>
        <InputGroupInput
          placeholder="Search sites…"
          aria-label="Search sites"
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
        {q.length > 0 && (
          <InputGroupAddon align="inline-end">
            <InputGroupButton
              type="button"
              aria-label="Clear search"
              size="icon-xs"
              onClick={() => setQ('')}
            >
              <X aria-hidden />
            </InputGroupButton>
          </InputGroupAddon>
        )}
      </InputGroup>
      <Filters
        fields={fields}
        query={query}
        variant="basic"
        size="sm"
        showClear
        onQueryChange={(next) => {
          setQuery(next);
          emit(q, next);
        }}
      />
    </div>
  );
}
