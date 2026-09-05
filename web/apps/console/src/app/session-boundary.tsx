import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { api, ApiError, resetSessionContext, SESSION_CONTEXT_CHANGED, SESSION_CONTEXT_OBSERVED } from '@locintel/api';
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';

type Transition = (change: () => Promise<unknown>) => Promise<void>;
const TransitionContext = createContext<Transition | null>(null);
const newClient = () => new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
});

/** Existing writes finish with the old cookie before a session change is sent. */
async function finishMutations(client: QueryClient) {
  if (client.isMutating() === 0) return;
  await new Promise<void>((resolve) => {
    const unsubscribe = client.getMutationCache().subscribe(() => {
      if (client.isMutating() === 0) {
        unsubscribe();
        resolve();
      }
    });
  });
}

/** A session change owns a fresh cache and a fresh component tree, including forms. */
export function SessionBoundary({ children }: { children: ReactNode }) {
  const [client, setClient] = useState(newClient);
  const [generation, setGeneration] = useState(0);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const changing = useRef(false);
  const channel = useRef<BroadcastChannel | null>(null);
  const observedContext = useRef<string | undefined>(undefined);

  const transition = async (change: () => Promise<unknown>, announce = true) => {
    if (changing.current) return;
    changing.current = true;
    const writes = client.getMutationCache().getAll().filter((mutation) => mutation.state.status === 'pending');
    let transitionError: string | undefined;
    setPending(true);
    setError(undefined);
    try {
      await client.cancelQueries();
      await finishMutations(client);
      await client.cancelQueries();
      await change();
    } catch (cause) {
      transitionError = cause instanceof Error ? cause.message : 'Could not change session';
    } finally {
      // Even an unsuccessful response may have changed the cookie. Resolve /me
      // afresh instead of restoring cached tenant data under an uncertain session.
      client.clear();
      const uncertain = writes.map((mutation) => mutation.state.error)
        .find((cause) => cause instanceof ApiError && cause.outcomeUnknown);
      setError([transitionError, uncertain?.message].filter(Boolean).join(' ') || undefined);
      resetSessionContext();
      setClient(newClient());
      setGeneration((value) => value + 1);
      setPending(false);
      changing.current = false;
      if (announce) channel.current?.postMessage('changed');
    }
  };

  useEffect(() => {
    const probe = new AbortController();
    const updates = new BroadcastChannel('locintel-session');
    channel.current = updates;
    const refresh = () => { void transition(async () => {}, false); };
    updates.onmessage = (event) => {
      if (event.data !== observedContext.current) refresh();
    };
    const observed = (event: Event) => {
      const context = (event as CustomEvent<unknown>).detail;
      if (typeof context !== 'string') return;
      observedContext.current = context;
      updates.postMessage(context);
    };
    let checking = false;
    const checkSession = () => {
      if (checking || changing.current || document.visibilityState === 'hidden') return;
      checking = true;
      // Detect cookie changes from another app/login without accepting its data
      // into the old cache. The transport signals a mismatch to refresh above.
      void api.get('/me', { signal: probe.signal }).catch(() => {}).finally(() => { checking = false; });
    };
    window.addEventListener(SESSION_CONTEXT_CHANGED, refresh);
    window.addEventListener(SESSION_CONTEXT_OBSERVED, observed);
    window.addEventListener('focus', checkSession);
    document.addEventListener('visibilitychange', checkSession);
    return () => {
      probe.abort();
      window.removeEventListener(SESSION_CONTEXT_CHANGED, refresh);
      window.removeEventListener(SESSION_CONTEXT_OBSERVED, observed);
      window.removeEventListener('focus', checkSession);
      document.removeEventListener('visibilitychange', checkSession);
      updates.close();
      channel.current = null;
    };
  }, [client]);

  return (
    <TransitionContext.Provider value={transition}>
      {pending ? (
        <main className="p-12" role="status">Changing session; finishing pending work…</main>
      ) : (
        <QueryClientProvider key={generation} client={client}>
          {error && <p role="alert" className="p-4 text-destructive">{error}</p>}
          {children}
        </QueryClientProvider>
      )}
    </TransitionContext.Provider>
  );
}

export function useSessionTransition(): Transition {
  const transition = useContext(TransitionContext);
  if (!transition) throw new Error('SessionBoundary is required');
  return transition;
}
