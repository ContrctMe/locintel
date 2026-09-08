import { api } from '@locintel/api';

export const issueEvidenceDownload = (id: string, evidenceId: string) =>
  api.post('/api/cases/{id}/evidence/{evidenceId}/download', undefined, { path: { id, evidenceId } });
