#!/usr/bin/env node
// Local rehearsal only. FIXTURE comes from fleet-bench.mjs; PROOF contains synthetic IDs/hashes.
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { setTimeout as sleep } from 'node:timers/promises';

const [mode] = process.argv.slice(2);
assert(['seed', 'verify', 'work'].includes(mode), 'usage: FIXTURE=... PROOF=... node tools/restore-proof.mjs seed|verify|work');
const fixture = JSON.parse(readFileSync(process.env.FIXTURE, 'utf8'));
const base = process.env.RESTORE_BASE || fixture.base;
assert(['127.0.0.1', 'localhost'].includes(new URL(base).hostname), 'local rehearsal only');
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
async function request(path, method = 'GET', body) {
  const response = await fetch(base + path, {
    method, signal: AbortSignal.timeout(30_000),
    headers: { cookie: fixture.cookie, origin: base, 'content-type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  assert(response.ok, `${method} ${path}: ${response.status}`);
  const text = await response.text();
  return text ? JSON.parse(text) : null;
}
async function waitForFile(id, predicate) {
  for (let i = 0; i < 150; i++) {
    const file = await request(`/api/files/${id}`);
    if (predicate(file)) return file;
    await sleep(200);
  }
  assert.fail('file state did not settle within 30 seconds');
}
let proof;
if (mode === 'seed') {
  const bytes = Buffer.from('Synthetic LocIntel restore evidence\n');
  const upload = await request('/api/files', 'POST', {
    name: 'restore-proof.txt', contentType: 'text/plain', sizeBytes: bytes.length,
  });
  const put = await fetch(new URL(upload.ticket.url, base), {
    method: 'PUT', headers: upload.ticket.headers, body: bytes, signal: AbortSignal.timeout(30_000),
  });
  assert(put.ok, `upload: ${put.status}`);
  await request(`/api/files/${upload.fileId}/complete`, 'POST');
  await waitForFile(upload.fileId, file => file.status === 'Clean');
  const opened = await request('/api/cases', 'POST', { title: 'Restore proof' });
  const evidence = await request(`/api/cases/${opened.id}/evidence`, 'POST', { fileId: upload.fileId });
  await request(`/api/cases/${opened.id}/hold`, 'POST', { hold: true });
  await waitForFile(upload.fileId, file => file.legalHold);
  const custody = await request(`/api/cases/${opened.id}/custody`);
  proof = { caseId: opened.id, evidenceId: evidence.id, fileId: upload.fileId, sha256: digest(bytes),
    custody: custody.events.map(event => ({ id: event.id, action: event.action })) };
  writeFileSync(process.env.PROOF, JSON.stringify(proof, null, 2), { mode: 0o600 });
} else if (mode === 'verify') {
  proof = JSON.parse(readFileSync(process.env.PROOF, 'utf8'));
  const detail = await request(`/api/cases/${proof.caseId}`);
  assert.equal(detail.legalHold, true);
  assert.equal(detail.evidence[0].fileId, proof.fileId);
  assert.equal(detail.evidence[0].fileName, 'restore-proof.txt');
  const custody = await request(`/api/cases/${proof.caseId}/custody`);
  assert.deepEqual(custody.events.map(event => ({ id: event.id, action: event.action })), proof.custody);
  assert.equal((await request(`/api/files/${proof.fileId}`)).legalHold, true);
  const ticket = await request(`/api/cases/${proof.caseId}/evidence/${proof.evidenceId}/download`, 'POST');
  const download = await fetch(new URL(ticket.url, base), { signal: AbortSignal.timeout(30_000) });
  assert(download.ok, `download: ${download.status}`);
  assert.equal(digest(Buffer.from(await download.arrayBuffer())), proof.sha256);
  // The original session must still decrypt using the restored data-protection keys.
  const session = await fetch(base + '/me', { headers: { cookie: fixture.cookie }, redirect: 'manual' });
  assert.equal(session.status, 200);
  assert.equal((await session.json()).activeOrg, fixture.orgId);
} else {
  proof = JSON.parse(readFileSync(process.env.PROOF, 'utf8'));
  const refused = await fetch(`${base}/api/files/${proof.fileId}`, {
    method: 'DELETE', headers: { cookie: fixture.cookie, origin: base }, signal: AbortSignal.timeout(30_000),
  });
  assert.equal(refused.status, 409, 'restored legal hold must prevent deletion');
  await request(`/api/cases/${proof.caseId}/hold`, 'POST', { hold: false });
  await waitForFile(proof.fileId, file => !file.legalHold);
  await request(`/api/cases/${proof.caseId}/hold`, 'POST', { hold: true });
  await waitForFile(proof.fileId, file => file.legalHold);
}
console.log(JSON.stringify({ mode, caseId: proof.caseId, fileId: proof.fileId, sha256: proof.sha256, result: 'pass' }));
