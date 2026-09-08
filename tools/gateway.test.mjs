#!/usr/bin/env node
// Real gateway/app/Redis acceptance controls. Run after gateway-stack.sh up.
// Uses only the isolated local reference; policy is restored even on failure.
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { setTimeout as sleep } from 'node:timers/promises';
import { performance } from 'node:perf_hooks';
import { fetchWithHost } from '../web/apps/public/src/upstream.ts';

const compose = (...args) => execFileSync('tools/gateway-stack.sh', ['compose', ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
const containers = () => compose('ps', '--format', 'json').split('\n').filter(Boolean).flatMap(line => JSON.parse(line));
const gatewayUrls = () => containers()
  .filter(row => row.Service === 'gateway')
  .flatMap(row => row.Publishers.filter(port => port.TargetPort === 8080).map(port => `http://127.0.0.1:${port.PublishedPort}`));
let gateways = gatewayUrls();
const originalGatewayCount = gateways.length;
const originalApiCount = containers().filter(row => row.Service === 'api').length;
let scaled = false;
assert(gateways.length >= 2, 'requires at least two gateway replicas');
const outputDir = process.env.GATEWAY_OUTPUT ?? 'coverage/gateway';
const runtime = process.env.GATEWAY_RUNTIME ?? `${outputDir}/runtime`;
const fixture = JSON.parse(readFileSync(process.env.FLEET_BENCH_FIXTURE ?? `${outputDir}/fixture.local.json`, 'utf8'));
const policyPath = `${runtime}/ratelimit/config/policy.yaml`;
const originalPolicy = readFileSync(policyPath, 'utf8');
const bootstrapPath = `${runtime}/envoy.yaml`;
const originalBootstrap = readFileSync(bootstrapPath, 'utf8');
let identityFault = false;
const summary = { startedAt: new Date().toISOString(), passed: false, gateways, controls: [] };
const base = gateways[0];
const cookies = new Map(fixture.cookie.split('; ').map(pair => { const at = pair.indexOf('='); return [pair.slice(0, at), pair.slice(at + 1)]; }));

async function owner(path, init = {}) {
  const response = await fetch(base + path, { ...init, redirect: 'manual', signal: AbortSignal.timeout(10000), headers: {
    cookie: [...cookies].map(([key, value]) => `${key}=${value}`).join('; '), origin: base,
    'content-type': 'application/json', ...init.headers,
  } });
  for (const raw of response.headers.getSetCookie()) {
    const pair = raw.split(';')[0], at = pair.indexOf('=');
    cookies.set(pair.slice(0, at), pair.slice(at + 1));
  }
  assert(response.ok, `${path}: ${response.status}`);
  const body = await response.text();
  return body ? JSON.parse(body) : null;
}
async function newKey(name) {
  const roles = await owner('/api/roles');
  return owner('/api/api-keys', { method: 'POST', body: JSON.stringify({ name, roleId: roles.find(role => role.name === 'Owner').id }) });
}
async function request(index, token, path = '/api/sites?limit=1', extra = {}) {
  const start = performance.now();
  const response = await fetch(gateways[index % gateways.length] + path, { redirect: 'manual', signal: AbortSignal.timeout(5000), headers: { authorization: `Bearer ${token}`, ...extra } });
  const body = await response.text();
  for (const header of ['x-premise-gateway-key', 'x-premise-fairness-partition', 'x-premise-client-ip'])
    assert.equal(response.headers.get(header), null, `private header exposed: ${header}`);
  return { status: response.status, retry: response.headers.get('retry-after'), instance: response.headers.get('x-premise-instance'), latencyMs: performance.now() - start, body };
}
async function until(check, description, timeoutMs = 10000) {
  const deadline = performance.now() + timeoutMs;
  do { if (await check()) return; await sleep(100); } while (performance.now() < deadline);
  throw new Error(`Timed out: ${description}`);
}
function policy(noisyOrg, quietOrg, noisyMinute, quietMinute, sustainedUnit = 'minute') {
  return { domain: 'premise', descriptors: ['burst', 'sustained'].map(window => ({ key: 'window', value: window,
    descriptors: [
      { key: 'partition', value: `org:${noisyOrg}`, rate_limit: { unit: window === 'burst' ? 'second' : sustainedUnit, requests_per_unit: window === 'burst' ? 1000 : noisyMinute } },
      { key: 'partition', value: `org:${quietOrg}`, rate_limit: { unit: window === 'burst' ? 'second' : sustainedUnit, requests_per_unit: window === 'burst' ? 1000 : quietMinute } },
      { key: 'partition', rate_limit: { unit: window === 'burst' ? 'second' : 'minute', requests_per_unit: 10000 } },
    ] })) };
}

try {
  const publicResponse = await fetchWithHost(base + '/public/sites?limit=1', { signal: AbortSignal.timeout(10000),
    headers: { Host: fixture.guestHost, 'X-Forwarded-Host': 'untrusted.invalid' } });
  assert.equal(publicResponse.status, 200, 'public SSR must preserve the tenant Host through the gateway');
  assert((await publicResponse.json()).items.length > 0, 'tenant public data must survive forwarded-header sanitization');
  summary.controls.push({ name: 'public tenant Host preserved; forged forwarded host ignored', passed: true });
  const noisyOrg = (await owner('/me')).activeOrg;
  const secondKey = await newKey(`gateway-second-${Date.now()}`);
  const runId = Date.now();
  const created = await owner('/api/orgs', { method: 'POST', body: JSON.stringify({ name: `Gateway quiet tenant ${runId}`, slug: `gateway-quiet-${runId}` }) });
  const quietOrg = created.orgId;
  await until(async () => (await owner('/me')).organizations.some(org => org.id === quietOrg), 'quiet tenant membership');
  await owner('/auth/switch-org', { method: 'POST', body: JSON.stringify({ orgId: quietOrg }) });
  const quietKey = await newKey(`gateway-quiet-${Date.now()}`);
  const keys = [fixture.token, secondKey.secret];
  if (process.env.FAIRNESS_FIXTURE)
    writeFileSync(process.env.FAIRNESS_FIXTURE, JSON.stringify({ noisyOrg, quietOrg, noisyKeys: keys, quietKey: quietKey.secret }), { mode: 0o600 });
  writeFileSync(policyPath, JSON.stringify(policy(noisyOrg, quietOrg, 20, 1000), null, 2));
  // This is an isolated, ephemeral counter database owned by this test stack.
  compose('restart', 'ratelimit');
  await until(async () => {
    try { return compose('exec', '-T', 'redis', 'wget', '-q', '-O', '-', 'http://ratelimit:8080/healthcheck') === 'OK'; }
    catch { return false; }
  }, 'limiter ready with policy loaded');
  // Service health does not prove each gateway has reconnected its gRPC client.
  for (let i = 0; i < gateways.length; i++)
    await until(async () => /^[1-9]\d*$/.test((await request(i, fixture.token)).retry), 'gateway connected to limiter');
  // Align after reconnecting; startup time must not consume this test's window.
  if (60000 - Date.now() % 60000 < 25000) await sleep(61000 - Date.now() % 60000);
  compose('exec', '-T', 'redis', 'redis-cli', 'FLUSHDB');

  const noisy = [], quiet = [];
  for (let i = 0; i < 40; i++) {
    noisy.push(await request(i, keys[i % keys.length]));
    if (i % 4 === 0) quiet.push(await request(i + 1, quietKey.secret));
  }
  summary.observed = { noisyOrg, fixtureOrg: fixture.orgId, finishedAt: new Date().toISOString(),
    noisy: noisy.map(({ status, retry, instance }) => ({ status, retry, instance })),
    quiet: quiet.map(({ status, retry, instance }) => ({ status, retry, instance })) };
  assert.equal(noisy.filter(response => response.status === 200).length, 20, 'tenant allowance must be shared across credentials and gateways');
  assert.equal(noisy.filter(response => response.status === 429).length, 20);
  assert(quiet.every(response => response.status === 200), 'quiet tenant was throttled');
  assert(noisy.filter(response => response.status === 429).every(response => /^[1-9]\d*$/.test(response.retry)), '429 needs one useful Retry-After value');
  const instances = new Set([...noisy, ...quiet].filter(response => response.status === 200).map(response => response.instance));
  assert.equal(instances.size, fixture.instances.length, 'business requests must reach every API replica');
  summary.controls.push({ name: 'shared tenant allowance and quiet neighbor', noisyAccepted: 20, noisyRejected: 20,
    quietAccepted: quiet.length, quietMaxLatencyMs: Math.max(...quiet.map(response => response.latencyMs)), instances: [...instances] });

  writeFileSync(policyPath, JSON.stringify(policy(noisyOrg, quietOrg, 5, 1), null, 2));
  await until(async () => (await request(0, quietKey.secret)).status === 429, 'hot policy update');
  assert.equal((await request(1, fixture.token)).status, 429, 'changing a limit must not reset the spent window');
  assert.equal((await request(0, fixture.token, '/api/sites?limit=1', { 'x-premise-fairness-partition': `org:${quietOrg}`, 'x-premise-gateway-key': 'forged' })).status, 429);
  summary.controls.push({ name: 'hot policy update preserves counters; spoofed partition ignored', passed: true });

  for (const path of ['/_gateway/identity', '/_GATEWAY/identity', '/_GaTeWaY/identity', '//_gateway/identity', '/%5fgateway/identity', '/_gateway%2fidentity']) {
    const result = await request(0, fixture.token, path);
    assert([400, 404].includes(result.status), `private identity path exposed: ${path} => ${result.status}`);
  }
  summary.controls.push({ name: 'private identity routes inaccessible through gateway', passed: true });

  compose('stop', 'ratelimit');
  assert.equal((await request(0, fixture.token)).status, 200, 'fairness service outage should fail open');
  compose('start', 'ratelimit');
  await until(async () => (await request(1, fixture.token)).status === 429, 'shared enforcement after limiter restart');
  summary.controls.push({ name: 'limiter outage fails open and restart preserves Redis counts', passed: true });


  // Use an hour window for this topology check so Docker startup cannot split
  // the measured allowance across a minute boundary. Defaults stay unchanged.
  while (3600000 - Date.now() % 3600000 <= 120000)
    await sleep(Math.min(60000, 3601000 - Date.now() % 3600000));
  writeFileSync(policyPath, JSON.stringify(policy(noisyOrg, quietOrg, 5, 1, 'hour'), null, 2));
  await until(async () => Number((await request(0, fixture.token)).retry) > 60, 'hour-window scaling policy loaded');
  await until(async () => (await request(0, fixture.token)).status === 429, 'scaling baseline allowance spent');
  scaled = true;
  compose('up', '-d', '--no-deps', '--scale', `api=${originalApiCount + 1}`, '--scale', `gateway=${originalGatewayCount + 1}`, 'api', 'gateway');
  // A 429 can be served through an old API while the new API is still booting.
  execFileSync('tools/gateway-stack.sh', ['wait-api'], { stdio: 'inherit' });
  gateways = gatewayUrls();
  assert.equal(gateways.length, originalGatewayCount + 1);
  for (let i = 0; i < gateways.length; i++) {
    let decision;
    await until(async () => {
      try {
        decision = await request(i, fixture.token);
        // A new gateway can still discover an unready identity endpoint. Keep
        // the first actual limiter decision; a 200 with quota metadata must fail
        // below, rather than consuming a renewed allowance until it reaches 429.
        return decision.status !== 503 && /^[1-9]\d*$/.test(decision.retry);
      } catch { return false; }
    }, 'scaled gateway connected', 30000);
    summary.scalingDecisions ??= [];
    summary.scalingDecisions.push({ gateway: i, status: decision.status, retry: decision.retry });
    assert.equal(decision.status, 429, 'scaling must not renew a spent tenant allowance');
  }
  summary.controls.push({ name: 'adding API and gateway replicas preserves spent tenant allowance',
    before: { api: originalApiCount, gateway: originalGatewayCount },
    after: { api: originalApiCount + 1, gateway: gateways.length }, passed: true });

  const brokenIdentity = originalBootstrap.replace(/(cluster_name: identity[\s\S]*?port_value: )8080/, (_, prefix) => `${prefix}9`);
  assert.notEqual(brokenIdentity, originalBootstrap, 'identity fault must change only its upstream port');
  identityFault = true;
  writeFileSync(bootstrapPath, brokenIdentity);
  compose('restart', 'gateway');
  gateways = gatewayUrls();
  assert.equal(JSON.parse(compose('exec', '-T', 'redis', 'wget', '-q', '--header=Host: localhost', '-O', '-', 'http://api:8080/livez')).status, 'alive');
  for (let i = 0; i < gateways.length; i++) {
    let response;
    await until(async () => {
      try { response = await request(i, fixture.token); return true; }
      catch { return false; }
    }, 'fault-injected gateway listener ready', 30000);
    assert.equal(response.status, 503, 'identity transport failure must fail closed');
  }
  writeFileSync(bootstrapPath, originalBootstrap);
  compose('restart', 'gateway');
  gateways = gatewayUrls();
  for (let i = 0; i < gateways.length; i++)
    await until(async () => {
      try { return (await request(i, fixture.token)).status === 429; }
      catch { return false; }
    }, 'identity service recovered', 30000);
  identityFault = false;
  summary.controls.push({ name: 'isolated identity transport failure fails closed; healthy application and enforcement recover', passed: true });
  compose('stop', 'redis');
  assert.equal((await request(0, fixture.token)).status, 200, 'Redis outage should fail open');
  compose('start', 'redis');
  await until(async () => {
    try { return compose('exec', '-T', 'redis', 'redis-cli', 'PING') === 'PONG'; }
    catch { return false; }
  }, 'Redis restarted');
  await until(async () => {
    const response = await request(0, fixture.token);
    return response.status === 200 && /^[1-9]\d*$/.test(response.retry);
  }, 'fresh allowance after ephemeral Redis restart');
  await until(async () => {
    const decisions = await Promise.all(Array.from({ length: 20 }, (_, i) => request(i, fixture.token)));
    return decisions.every(response => response.status === 429);
  }, 'consistent enforcement resumes after Redis restart', 30000);
  summary.controls.push({ name: 'Redis outage fails open; restart renews ephemeral allowance and resumes enforcement', passed: true });

  summary.passed = true;
  console.log(JSON.stringify(summary, null, 2));
} finally {
  writeFileSync(policyPath, originalPolicy);
  if (identityFault) {
    writeFileSync(bootstrapPath, originalBootstrap);
    compose('restart', 'gateway');
  }
  compose('start', 'redis', 'ratelimit');
  if (scaled) compose('up', '-d', '--no-deps', '--scale', `api=${originalApiCount}`, '--scale', `gateway=${originalGatewayCount}`, 'api', 'gateway');
  writeFileSync(`${outputDir}/fairness-results.json`, JSON.stringify(summary, null, 2));
}
