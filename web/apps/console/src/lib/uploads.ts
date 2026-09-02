import { api } from '@locintel/api';

type Ticket = { url: string; method: string; headers: Record<string, string> };

/** The full ticket flow (ADR 19): create -> direct PUT to storage -> complete -> poll scan. */
export async function uploadFile(
  file: File,
  contentType: string,
  onPhase?: (phase: string) => void,
): Promise<string> {
  onPhase?.('Requesting upload ticket…');
  const created = await api.post<{ fileId: string; ticket: Ticket }>('/api/files', {
    name: file.name,
    contentType,
    sizeBytes: file.size,
  });
  onPhase?.('Uploading to storage…');
  await fetch(created.ticket.url, {
    method: created.ticket.method,
    body: file,
    credentials: 'include',
  });
  await api.post(`/api/files/${created.fileId}/complete`);
  onPhase?.('Scanning…');
  for (let attempt = 0; attempt < 60; attempt++) {
    const { items: files } = await api.get<{
      items: { id: string; status: string }[];
      total: number;
      nextOffset: number | null;
    }>('/api/files');
    const status = files.find((f) => f.id === created.fileId)?.status;
    if (status === 'Clean') return created.fileId;
    if (status === 'Quarantined') throw new Error('file was quarantined by the scanner');
    await new Promise((resolve) => setTimeout(resolve, 300));
  }
  throw new Error('scan timed out');
}
