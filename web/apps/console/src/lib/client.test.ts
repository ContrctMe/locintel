import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, api, apiPlanLimit, apiProblem, isTransient, resetSessionContext } from '@locintel/api';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  resetSessionContext();
});

describe('API response normalization', () => {
  it('passes a caller-owned idempotency key through to report submission', async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ id: 'report', state: 'Queued' }));
    vi.stubGlobal('fetch', fetchMock);
    await api.post('/api/reports', { reportType: 'site', mode: 'single', selection: 'selected', siteIds: [], options: {} }, { idempotencyKey: 'stable-report-key' });
    expect(fetchMock).toHaveBeenCalledWith('/api/reports', expect.objectContaining({ headers: expect.objectContaining({ 'Idempotency-Key': 'stable-report-key' }) }));
  });
  it('treats only a 503 the server timed as safe to repeat', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      Response.json({ error: 'Another report is being admitted.', code: 'admission_busy' },
        { status: 503, headers: { 'Retry-After': '2' } })));
    const timed = (await api.get('/api/reports').catch((e: unknown) => e)) as ApiError;
    expect(timed.retryAfterSeconds).toBe(2);
    expect(isTransient(timed)).toBe(true);
    // A bare 503 can come from in front of the API, after the write landed.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 503 })));
    const bare = (await api.get('/api/reports').catch((e: unknown) => e)) as ApiError;
    expect(bare.retryAfterSeconds).toBeUndefined();
    expect(isTransient(bare)).toBe(false);
  });

  it('reads a plan limit out of the gate\'s 402 so callers can offer the plan', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(
      { error: 'plan limit reached', code: 'reports.monthly', limit: 1000, current: 1000 },
      { status: 402 })));
    const error = (await api.get('/api/reports/quota').catch((e: unknown) => e)) as ApiError;
    expect(apiPlanLimit(error.status, error.body)).toEqual({ code: 'reports.monthly', limit: 1000, current: 1000 });
    expect(error.message).toContain('1000 of 1000 used');
    expect(apiPlanLimit(500, { code: 'x' })).toBeUndefined();
  });

  it('refuses browser session bootstrap without the context precondition header', async () => {
    vi.stubGlobal('window', new EventTarget());
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json({ tier: 'guest' })));
    await expect(api.get('/me')).rejects.toMatchObject({
      name: 'ApiError', message: expect.stringMatching(/Session verification unavailable/),
    });
  });

  it('propagates query cancellation to the network without retrying', async () => {
    const cancelled = new AbortController();
    let networkSignal: AbortSignal | null | undefined;
    vi.stubGlobal('fetch', vi.fn(async (_url: unknown, init?: RequestInit) => {
      networkSignal = init?.signal;
      return new Promise<Response>((_resolve, reject) => {
        networkSignal?.addEventListener('abort', () => reject(networkSignal?.reason));
      });
    }));
    const request = api.get('/healthz', { signal: cancelled.signal });
    const reason = new DOMException('query cancelled', 'AbortError');
    cancelled.abort(reason);
    await expect(request).rejects.toBe(reason);
    expect(networkSignal?.aborted).toBe(true);
  });

  it('does not send an already-cancelled request', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    const signal = AbortSignal.abort();
    await expect(api.post('/auth/logout', undefined, { signal })).rejects.toBe(signal.reason);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('bounds writes and reports an unknown outcome without retrying', async () => {
    const deadline = new AbortController();
    const timeout = vi.spyOn(AbortSignal, 'timeout').mockReturnValue(deadline.signal);
    const fetchMock = vi.fn(async (_url: unknown, init?: RequestInit) => {
      if (!init?.signal) throw new Error('request has no deadline');
      return new Promise<Response>((_resolve, reject) => {
        init.signal!.addEventListener('abort', () => reject(init.signal!.reason), { once: true });
      });
    });
    vi.stubGlobal('fetch', fetchMock);
    const request = api.post('/auth/logout');
    deadline.abort(new DOMException('deadline', 'TimeoutError'));
    await expect(request).rejects.toMatchObject({
      name: 'ApiError', outcomeUnknown: true,
      message: expect.stringMatching(/may have completed.*Refresh before retrying/),
    });
    expect(timeout).toHaveBeenCalledWith(30_000);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('normalizes response-body failures, including uncertain writes', async () => {
    const body = new ReadableStream({
      start(controller) { controller.error(new TypeError('connection closed')); },
    });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(body)));
    await expect(api.post('/auth/logout')).rejects.toMatchObject({
      name: 'ApiError', status: 0, outcomeUnknown: true,
    });
  });

  it('keeps the deadline active while consuming the response body', async () => {
    const deadline = new AbortController();
    vi.spyOn(AbortSignal, 'timeout').mockReturnValue(deadline.signal);
    const body = new ReadableStream({
      start(controller) {
        deadline.signal.addEventListener('abort', () => controller.error(deadline.signal.reason));
      },
    });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(body)));
    const request = api.get('/healthz');
    await Promise.resolve();
    deadline.abort(new DOMException('deadline', 'TimeoutError'));
    await expect(request).rejects.toMatchObject({
      name: 'ApiError', status: 0, message: expect.stringMatching(/timed out/),
    });
  });

  it.each([null, 42, true, {}, [null], ['Required', 42]])(
    'rejects malformed validation values without throwing: %j', (value) => {
      const body = { errors: { name: value } };
      expect(apiProblem(body)).toBeUndefined();
      expect(new ApiError(400, body)).toMatchObject({ name: 'ApiError', message: 'API 400' });
    },
  );

  it('preserves valid validation strings and arrays', () => {
    expect(new ApiError(400, { errors: { name: ['', 'Required'] } }).message).toBe('Required');
    expect(new ApiError(400, { errors: { name: 'Required' } }).message).toBe('Required');
  });

  it('handles empty successful responses', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    await expect(api.post('/auth/logout')).resolves.toBeUndefined();
  });

  it('keeps non-JSON error bodies inside ApiError', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response('upstream unavailable', { status: 502 })),
    );
    await expect(api.get('/healthz')).rejects.toMatchObject({
      name: 'ApiError',
      status: 502,
      body: 'upstream unavailable',
      message: 'upstream unavailable',
    });
  });

  it('normalizes network and permission failures', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));
    await expect(api.get('/healthz')).rejects.toMatchObject({
      status: 0,
      message: 'Unable to reach the server',
    });

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 403 })));
    await expect(api.get('/healthz')).rejects.toMatchObject({ message: 'Permission denied' });
  });

  it('accepts only the actionable problem shape', () => {
    expect(apiProblem({ error: 'conflict', traceId: 'trace-1' })).toEqual({
      error: 'conflict',
      traceId: 'trace-1',
    });
    expect(apiProblem({ error: 42 })).toBeUndefined();
    expect(new ApiError(409, undefined).message).toBe(
      'The request conflicts with a newer change',
    );
  });
});
