import { useCallback, useState } from 'react';

/**
 * A per-browser preference (the table-or-map view, the basemap, the site a
 * phone opened last): read once, written on change, and never a reason to
 * fail - storage can be absent, full, or refused, and the fallback is the
 * answer then. Stored as JSON under one prefix so a key is greppable.
 * Private session data stays in React Query; this store is for choices.
 */
const PREFIX = 'locintel.';

export function readPreference<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(PREFIX + key);
    return raw === null ? fallback : (JSON.parse(raw) as T);
  } catch {
    return fallback;
  }
}

export function writePreference<T>(key: string, value: T): void {
  try {
    if (value === null || value === undefined) localStorage.removeItem(PREFIX + key);
    else localStorage.setItem(PREFIX + key, JSON.stringify(value));
  } catch {
    // a preference; losing it costs one click
  }
}

/** State that starts from the stored preference and writes back on change. */
export function usePreference<T>(key: string, fallback: T): [T, (next: T) => void] {
  const [value, setValue] = useState<T>(() => readPreference(key, fallback));
  const set = useCallback(
    (next: T) => {
      setValue(next);
      writePreference(key, next);
    },
    [key],
  );
  return [value, set];
}
