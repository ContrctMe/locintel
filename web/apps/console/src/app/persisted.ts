/** Tab-local cache for non-sensitive process metadata only (the API version). */
type Stored<T> = { data: T; at: number };

const MAX_BYTES = 256 * 1024;

export function persisted<T>(key: string) {
  const fullKey = `locintel.cache.${key}`;
  return {
    read(): Stored<T> | null {
      try {
        const raw = sessionStorage.getItem(fullKey);
        if (!raw) return null;
        const parsed = JSON.parse(raw) as Stored<T>;
        return typeof parsed.at === 'number' ? parsed : null;
      } catch {
        return null;
      }
    },
    write(data: T) {
      try {
        const json = JSON.stringify({ data, at: Date.now() });
        // Keep metadata below the browser's small tab-storage quota.
        if (json.length > MAX_BYTES) {
          sessionStorage.removeItem(fullKey);
          return;
        }
        sessionStorage.setItem(fullKey, json);
      } catch {
        // a full or blocked store only costs the next reload a request
      }
    },
  };
}

/** Remove private caches written by older builds, including on a fresh login. */
export function clearPrivateCache() {
  try {
    for (const key of Object.keys(sessionStorage))
      if (key.startsWith('locintel.cache.hierarchy.')) sessionStorage.removeItem(key);
  } catch {
    // A blocked browser store cannot expose its contents to this application.
  }
}
