import { api } from '@locintel/api';
import { Button } from '@locintel/ui';
import { useSessionTransition } from '../session-boundary';

export function ImpersonationBanner({ orgName, expiresAt }: { orgName: string; expiresAt: string }) {
  const changeSession = useSessionTransition();
  const ends = new Date(expiresAt).toLocaleTimeString(undefined, {
    hour: 'numeric',
    minute: '2-digit',
  });
  return (
    <div className="flex items-center justify-between gap-3 bg-warning px-4 py-2 text-sm text-warning-foreground">
      <span>
        <span className="font-semibold">Support session:</span> impersonating {orgName} · ends{' '}
        {ends}
      </span>
      <Button
        variant="outline"
        size="sm"
        onClick={async () => {
          await changeSession(() => api.post('/auth/impersonation/stop'));
        }}
      >
        Stop impersonating
      </Button>
    </div>
  );
}
