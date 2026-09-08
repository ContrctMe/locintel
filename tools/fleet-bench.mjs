#!/usr/bin/env node
// The load baseline against the replica stack: sign in as the seeded owner,
// mint an API key, and run tools/load-baseline.mjs through the proxy. One
// row per target; run it at 1, 2 and 4 replicas and compare (the scaling
// table in docs/scaling.md). Numbers from one laptop are RELATIVE: every
// replica shares the same cores and the same Postgres.
//
//   node tools/fleet-bench.mjs <proxyBase> [seconds] [concurrency] [replicas]
//
import { writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';

const [base, seconds = '15', concurrency = '32', replicas = '?'] = process.argv.slice(2);
if (!base) {
  console.error('usage: node tools/fleet-bench.mjs <proxyBase> [seconds] [concurrency] [replicas]');
  process.exit(1);
}

// a cookie jar the size of the sign-in flow: follow the local provider's redirects by hand
const jar = new Map();
const cookieHeader = () => [...jar].map(([k, v]) => `${k}=${v}`).join('; ');
async function follow(url, init = {}) {
  for (let hop = 0; hop < 8; hop++) {
    const res = await fetch(url, { ...init, signal: AbortSignal.timeout(30_000), redirect: 'manual', headers: { ...init.headers, cookie: cookieHeader() } });
    for (const raw of res.headers.getSetCookie()) {
      const [pair] = raw.split(';');
      const eq = pair.indexOf('=');
      jar.set(pair.slice(0, eq), pair.slice(eq + 1));
    }
    if (res.status >= 300 && res.status < 400 && res.headers.get('location')) {
      url = new URL(res.headers.get('location'), url).toString();
      init = { method: 'GET' };
      continue;
    }
    return res;
  }
  throw new Error('too many redirects');
}

// The fixture operator lifts only the domain site limit when seeding data.
const opMe = await follow(`${base}/auth/login?hint=operator%40premise.local&returnUrl=%2Fme`);
if (!opMe.ok) throw new Error(`operator sign-in failed: ${opMe.status}`);
const orgs = await (await follow(`${base}/api/operator/orgs`)).json();
const acme = orgs.find((o) => o.slug === 'acme-dev') ?? orgs.find((o) => !o.isPlatform);
const seedSites = Number(process.env.SEED_SITES || 0);
if (!Number.isInteger(seedSites) || seedSites < 0) throw new Error('SEED_SITES must be non-negative');
if (seedSites) {
  const liftedSites = await follow(`${base}/api/operator/orgs/${acme.id}/entitlements/sites.max`, {
    method: 'PUT', headers: { 'content-type': 'application/json', origin: base },
    body: JSON.stringify({ value: String(seedSites + 1000) }),
  });
  if (!liftedSites.ok) throw new Error(`site quota lift failed: ${liftedSites.status}`);
}
jar.clear();

const me = await follow(`${base}/auth/login?hint=alice%40acme.test&returnUrl=%2Fme`);
if (!me.ok) throw new Error(`sign-in failed: ${me.status}`);
const orgId = (await me.json()).activeOrg;
const roles = await (await follow(`${base}/api/roles`)).json();
const roleId = roles.find((r) => r.name === 'Owner')?.id ?? roles[0]?.id;
const key = await follow(`${base}/api/api-keys`, {
  method: 'POST',
  headers: { 'content-type': 'application/json', origin: base },
  body: JSON.stringify({ name: `fleet-bench-${Date.now()}`, roleId }),
});
if (!key.ok) throw new Error(`api key failed: ${key.status} ${await key.text()}`);
const { secret } = await key.json();

// Prepare credentials only for the isolated Development fixture; never print them.
if (process.env.FLEET_BENCH_FIXTURE) {
  if (seedSites) {
    const hierarchy = await (await follow(`${base}/api/hierarchy`)).json();
    const root = hierarchy.nodes.find(n => n.depth === 0);
    let next = 0;
    await Promise.all(Array.from({ length: 8 }, async () => {
      while (next < seedSites) {
        const i = next++;
        const created = await fetch(`${base}/api/sites`, {
          method: 'POST', signal: AbortSignal.timeout(30_000),
          headers: { authorization: `Bearer ${secret}`, 'content-type': 'application/json' },
          body: JSON.stringify({ nodeId: root.id, name: `Capacity site ${String(i).padStart(6, '0')}`,
            timeZone: 'America/Chicago', city: 'Benchmark', latitude: 26 + (i % 97) / 97 * 22,
            longitude: -124 + (i % 89) / 89 * 57 }),
        });
        if (!created.ok) throw new Error(`seed failed: ${created.status} ${await created.text()}`);
        await created.arrayBuffer();
      }
    }));
    console.log(`Seeded ${seedSites} sites through the real API`);
  }
  const instances = new Set();
  let siteId;
  // API readiness can precede gateway DNS/health discovery. This is fixture
  // preparation only; timed load never retries or excludes failed responses.
  const discoveryDeadline = Date.now() + 30_000;
  do {
    instances.clear();
    for (let i = 0; i < Number(replicas) * 4; i++) {
      const response = await fetch(`${base}/api/sites?limit=1`, {
        headers: { authorization: `Bearer ${secret}` }, signal: AbortSignal.timeout(30_000),
      });
      if (!response.ok) throw new Error(`fixture probe failed: ${response.status}`);
      instances.add(response.headers.get('x-premise-instance'));
      siteId = (await response.json()).items[0]?.id;
    }
    if (siteId && !instances.has(null) && instances.size === Number(replicas)) break;
    await sleep(200);
  } while (Date.now() < discoveryDeadline);
  if (!siteId || instances.has(null) || instances.size !== Number(replicas)) throw new Error('incomplete fleet fixture');
  writeFileSync(process.env.FLEET_BENCH_FIXTURE, JSON.stringify({ base, token: secret, cookie: cookieHeader(), siteId, orgId,
    guestHost: 'acme-dev.localhost', instances: [...instances] }), { mode: 0o600 });
  process.exit(0);
}

console.log(`\n== ${replicas} replica(s), ${seconds}s x ${concurrency} concurrent, through ${base}`);
const baseline = path.join(path.dirname(fileURLToPath(import.meta.url)), 'load-baseline.mjs');
const run = spawnSync(process.execPath, [baseline, base, secret, seconds, concurrency], { stdio: 'inherit' });
process.exit(run.status ?? 1);
