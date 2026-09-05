import { afterEach, expect, it, vi } from 'vitest';
import { uploadFile } from './uploads';

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

it('polls the uploaded file directly rather than searching a paginated listing', async () => {
  const fetchMock = vi.fn(async (url: unknown, init?: RequestInit) => {
    if (url === '/api/files' && init?.method === 'POST') return Response.json({
      fileId: 'file-1', ticket: { url: 'https://storage.test/upload', method: 'PUT', headers: {} },
    });
    if (url === 'https://storage.test/upload') return new Response(null, { status: 200 });
    if (url === '/api/files/file-1/complete') return new Response(null, { status: 202 });
    if (url === '/api/files/file-1') return Response.json({ id: 'file-1', status: 'Clean' });
    throw new Error('scan polling must not depend on a listing page');
  });
  vi.stubGlobal('fetch', fetchMock);
  await expect(uploadFile(new File(['data'], 'file.txt'), 'text/plain')).resolves.toBe('file-1');
  expect(fetchMock.mock.calls.at(-1)?.[0]).toBe('/api/files/file-1');
});

it('bounds direct storage uploads and never completes a timed-out upload', async () => {
  const deadline = new AbortController();
  const nativeTimeout = AbortSignal.timeout.bind(AbortSignal);
  const timeout = vi.spyOn(AbortSignal, 'timeout').mockImplementation((ms) =>
    ms === 120_000 ? deadline.signal : nativeTimeout(ms),
  );
  let storageStarted!: () => void;
  const started = new Promise<void>((resolve) => { storageStarted = resolve; });
  const fetchMock = vi.fn(async (url: unknown, init?: RequestInit) => {
    if (url === '/api/files') return Response.json({
      fileId: 'file-1', ticket: { url: 'https://storage.test/upload', method: 'PUT', headers: {} },
    });
    storageStarted();
    if (!init?.signal) throw new Error('storage upload has no deadline');
    return new Promise<Response>((_resolve, reject) => {
      init.signal!.addEventListener('abort', () => reject(init.signal!.reason));
    });
  });
  vi.stubGlobal('fetch', fetchMock);
  const request = uploadFile(new File(['data'], 'file.txt'), 'text/plain');
  const result = expect(request).rejects.toMatchObject({
    name: 'ApiError', outcomeUnknown: true, message: expect.stringMatching(/may have completed/),
  });
  await started;
  deadline.abort(new DOMException('deadline', 'TimeoutError'));
  await result;
  expect(timeout).toHaveBeenCalledWith(120_000);
  expect(fetchMock.mock.calls.map(([url]) => url)).toEqual(['/api/files', 'https://storage.test/upload']);
});
