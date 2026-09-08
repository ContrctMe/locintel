// Executable acceptance-harness controls: a bad response or latency breach must fail.
// K6=/path/to/k6 node tools/capacity.test.mjs
import assert from 'node:assert/strict';
import http from 'node:http';
import { spawn } from 'node:child_process';
import { mkdtempSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const dir = mkdtempSync(path.join(tmpdir(), 'premise-capacity-control-'));
let mode = 'good';
const server = http.createServer((req, res) => {
  const answer = () => {
    res.writeHead(mode === 'error' ? 503 : 200, { 'content-type': 'application/json', 'X-Premise-Instance': 'control' });
    res.end(JSON.stringify(mode === 'invalid' ? { error: 'not a page' } : { items: [{ id: 'site' }] }));
  };
  ['slow', 'dropped'].includes(mode) ? setTimeout(answer, 200) : answer();
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const fixture = path.join(dir, 'fixture.local.json');
writeFileSync(fixture, JSON.stringify({ base: `http://127.0.0.1:${server.address().port}`, token: 'control', siteId: 'site', instances: ['control'] }), { mode: 0o600 });
try {
  for (const current of ['good', 'error', 'invalid', 'slow', 'dropped', 'imbalance']) {
    mode = current;
    const config = JSON.parse(readFileSync(fixture));
    config.instances = current === 'imbalance' ? ['control', 'missing'] : ['control'];
    writeFileSync(fixture, JSON.stringify(config));
    const child = spawn(process.env.K6 || 'k6', ['run', '--quiet', fileURLToPath(new URL('./capacity.k6.js', import.meta.url))], {
      env: { ...process.env, FIXTURE: fixture, RATE: current === 'dropped' ? '100' : '5', CAPACITY_SECONDS: '2', VUS: current === 'dropped' ? '1' : '4', ROUTES: 'sites', P95_MS: '100', P99_MS: '150', SUMMARY: path.join(dir, 'summary.json') },
      stdio: 'pipe',
    });
    let output = '';
    child.stdout.on('data', data => output += data);
    child.stderr.on('data', data => output += data);
    const code = await new Promise((resolve, reject) => { child.on('error', reject); child.on('exit', resolve); });
    assert.equal(code, current === 'good' ? 0 : 99, `${current}: ${output}`);
    const summary = JSON.parse(readFileSync(path.join(dir, 'summary.json')));
    if (current === 'dropped') assert.ok(summary.metrics.dropped_iterations.values.count > 0);
    console.log(`${current}: expected exit ${code}`);
  }
} finally {
  server.closeAllConnections();
  server.close();
  rmSync(dir, { recursive: true });
}
