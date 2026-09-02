import { api } from '@locintel/api';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { can, useMe } from '../session';

type Summary = { unread: number; activeBulletins: number; unacknowledgedBulletins: number };
type Prefs = { browserAlerts: boolean };

/**
 * "Push" without a push service: while the console is open, poll the alert
 * summary and raise a browser notification when unread alerts grow, for
 * members who opted in. A real push channel is a fork's transport.
 */
export function useAlertPings() {
  const { data: me } = useMe();
  const enabled = can(me, 'alerts:read');
  const { data: prefs } = useQuery({ queryKey: ['me', 'notifications'], queryFn: () => api.get<Prefs>('/api/me/notifications'), enabled });
  const { data: summary } = useQuery({
    queryKey: ['alerts', 'summary', 'pings'],
    queryFn: () => api.get<Summary>('/api/alerts/summary'),
    enabled: enabled && !!prefs?.browserAlerts,
    refetchInterval: 60_000,
  });
  const last = useRef<number | null>(null);
  useEffect(() => {
    if (!summary || !prefs?.browserAlerts) return;
    if (last.current !== null && summary.unread > last.current && typeof Notification !== 'undefined' && Notification.permission === 'granted') {
      const n = new Notification('New alert', { body: `${summary.unread} unread alert${summary.unread === 1 ? '' : 's'}` });
      n.onclick = () => { window.focus(); window.location.assign('/alerts'); };
    }
    last.current = summary.unread;
  }, [summary, prefs?.browserAlerts]);
}
