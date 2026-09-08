import { createContext, useContext, useMemo, type ReactNode } from 'react';
import { usePreference } from '../lib/preference';

/**
 * The console's global scope (direction B): a hierarchy node the whole app
 * reads under, chosen once in the shell's Scope panel. Null means the entire
 * grant. This narrows what pages ASK for (`under=` on the site list, ADR 49);
 * it never widens anything - gate 3 on the server still filters every query.
 * Per org, remembered in this browser only: a preference, not data.
 */
type Scope = { nodeId: string | null; setNodeId: (id: string | null) => void };

const ScopeContext = createContext<Scope | null>(null);

export function ScopeProvider({ orgId, children }: { orgId: string; children: ReactNode }) {
  // per org, this browser only: a preference, not data
  const [nodeId, setNodeId] = usePreference<string | null>(`scope.${orgId}`, null);
  const value = useMemo(() => ({ nodeId, setNodeId }), [nodeId, setNodeId]);
  return <ScopeContext.Provider value={value}>{children}</ScopeContext.Provider>;
}

export function useScope(): Scope {
  const scope = useContext(ScopeContext);
  if (!scope) throw new Error('useScope outside ScopeProvider');
  return scope;
}
