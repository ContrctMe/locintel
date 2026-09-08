import { api } from '@locintel/api';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { can, useMe } from '../session';

/**
 * "Push" without a push service: while the console is open, poll the alert
 * summary and raise a browser notification when unread alerts grow, for
 * members who opted in. A real push channel is a fork's transport.
 */
export function useAlertPings() {
  const { data: me } = useMe();
  const enabled = can(me, 'alerts:read');
  const { data: prefs } = useQuery({ queryKey: ['me', 'notifications'], queryFn: ({ signal }) => api.get('/api/me/notifications', { signal }), enabled });
  const { data: summary } = useQuery({
    queryKey: ['alerts', 'summary', 'pings'],
    queryFn: ({ signal }) => api.get('/api/alerts/summary', { signal }),
    enabled: enabled && !!prefs?.browserAlerts,
    refetchInterval: 60_000,
  });
  const last = useRef<number | null>(null);
  useEffect(() => {
    if (!summary || !prefs?.browserAlerts) return;
    const unread = Number(summary.unread);
    if (last.current !== null && unread > last.current && typeof Notification !== 'undefined' && Notification.permission === 'granted') {
      const n = new Notification('New alert', { body: `${unread} unread alert${unread === 1 ? '' : 's'}` });
      n.onclick = () => { window.focus(); window.location.assign('/alerts'); };
    }
    last.current = unread;
  }, [summary, prefs?.browserAlerts]);
}
