import { api, ApiError } from '@locintel/api';

/** The full ticket flow (ADR 19): create -> direct PUT to storage -> complete -> poll scan. */
export async function uploadFile(
  file: File,
  contentType: string,
  onPhase?: (phase: string) => void,
  siteId?: string,
): Promise<string> {
  // Bound the entire chain, not sixty independent polling deadlines. Larger
  // uploads need a deliberate limit change and slow-link acceptance tests.
  const signal = AbortSignal.timeout(120_000);
  onPhase?.('Requesting upload ticket…');
  const created = await api.post('/api/files', {
    name: file.name,
    contentType,
    sizeBytes: file.size,
    ...(siteId ? { siteId } : {}),
  }, { signal });
  onPhase?.('Uploading to storage…');
  let uploaded: Response;
  try {
    signal.throwIfAborted();
    uploaded = await fetch(created.ticket.url, {
      method: created.ticket.method,
      headers: created.ticket.headers,
      body: file,
      credentials: 'same-origin',
      signal,
    });
  } catch (cause) {
    throw new ApiError(0, {
      error: 'Upload interrupted. It may have completed at storage. Refresh before retrying.',
    }, cause, true);
  }
  if (!uploaded.ok)
    throw new Error(`Upload failed (HTTP ${uploaded.status}). Please try again.`);
  await api.post('/api/files/{id}/complete', undefined, { path: { id: created.fileId }, signal });
  onPhase?.('Scanning…');
  for (let attempt = 0; attempt < 60; attempt++) {
    const { status } = await api.get('/api/files/{id}', { path: { id: created.fileId }, signal });
    if (status === 'Clean') return created.fileId;
    if (status === 'Quarantined') throw new Error('file was quarantined by the scanner');
    await new Promise((resolve) => setTimeout(resolve, 300));
  }
  throw new Error('scan timed out');
}
