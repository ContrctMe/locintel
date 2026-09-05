import { api } from './client';

// Compile-time contract checks: these calls are never run.
export function clientContractChecks() {
  void api.get('/api/sites/{id}', { path: { id: 'site' } });
  void api.get('/api/sites/{id}', { path: { id: 'site' }, signal: new AbortController().signal });
  void api.get('/auth/signup', { query: { email: 'owner@example.com' } });
  void api.post('/api/sites', {
    name: 'HQ',
    timeZone: 'UTC',
    nodeId: '00000000-0000-0000-0000-000000000000',
  });

  // @ts-expect-error path initialization is required
  void api.get('/api/sites/{id}');
  // @ts-expect-error cancellation does not make required path parameters optional
  void api.get('/api/sites/{id}', { signal: new AbortController().signal });
  // @ts-expect-error required query initialization is required
  void api.get('/auth/signup');
  // @ts-expect-error a required JSON body cannot be omitted
  void api.post('/api/sites');
}
