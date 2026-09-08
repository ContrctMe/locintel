#!/usr/bin/env node
// Zero-dependency load baseline (maturity review: "Performance UNMEASURED").
// Node 20+; global fetch. Numbers from a laptop are RELATIVE costs - endpoint
// vs endpoint, ceiling behavior - never capacity planning.
//
//   node tools/load-baseline.mjs <baseUrl> <bearerToken> [seconds] [concurrency]
//
const [base, token, secondsArg, concurrencyArg] = process.argv.slice(2);
if (!base || !token) {
  console.error('usage: node tools/load-baseline.mjs <baseUrl> <premise_key> [seconds] [concurrency]');
  process.exit(1);
}
const seconds = Number(secondsArg ?? 10);
const concurrency = Number(concurrencyArg ?? 16);

const targets = [
  ['healthz (pipeline floor)', '/healthz'],
  ['sites paged (limit 50)', '/api/sites?limit=50'],
  ['sites search', '/api/sites?q=site&limit=50'],
  ['site detail', null], // resolved from the list below
  ['members', '/api/members'],
  ['listings feed (full fleet)', '/api/listings/feed'],
  ['public sites (unpaged ceiling)', '/public/sites'],
  ['public sites near-sort', '/public/sites?near=42.36,-71.05'],
];

const headers = { authorization: `Bearer ${token}` };

// Optional: a command that runs one SQL statement against the database the
// api uses (e.g. `docker exec fleet-pg psql -U postgres -d premise -tA -c`).
// With pg_stat_statements loaded, each target then reports how many
// statements the database ran per request, split into the request's own
// work and the background chatter (Wolverine's envelope polling).
import { execFileSync } from 'node:child_process';
const statsCmd = process.env.PREMISE_PG_STATS_CMD;
const sql = (statement) =>
  statsCmd ? execFileSync('sh', ['-c', `${statsCmd} "${statement.replaceAll('"', '\\"')}"`], { encoding: 'utf8' }).trim() : '';
const statsReset = () => statsCmd && sql('SELECT pg_stat_statements_reset()');
const statsRead = () => {
  if (!statsCmd) return null;
  const churnFilter =
    "(query ILIKE 'DISCARD ALL%' OR query ILIKE '%set_config%' OR query ILIKE '%rate_windows%' OR query IN ('BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED', 'COMMIT', 'ROLLBACK'))";
  const [app, background, appMs, churnMs] = sql(
    "SELECT coalesce(sum(calls) FILTER (WHERE query NOT ILIKE '%wolverine%' AND query NOT ILIKE '%pg_stat_statements%'), 0), " +
      "coalesce(sum(calls) FILTER (WHERE query ILIKE '%wolverine%'), 0), " +
      "coalesce(sum(total_exec_time) FILTER (WHERE query NOT ILIKE '%wolverine%' AND query NOT ILIKE '%pg_stat_statements%'), 0), " +
      `coalesce(sum(total_exec_time) FILTER (WHERE query NOT ILIKE '%wolverine%' AND ${churnFilter}), 0) FROM pg_stat_statements`,
  ).split('|').map(Number);
  // by time, not by calls: what the server actually spends its cores on
  const top = sql(
    "SELECT round(total_exec_time)::text || 'ms ' || calls || 'x ' || left(regexp_replace(query, '\\s+', ' ', 'g'), 60) FROM pg_stat_statements " +
      "WHERE query NOT ILIKE '%wolverine%' AND query NOT ILIKE '%pg_stat_statements%' ORDER BY total_exec_time DESC LIMIT 5",
  );
  const serverConnections = Number(sql("SELECT count(*) FROM pg_stat_activity WHERE backend_type = 'client backend' AND usename <> 'postgres'"));
  return { app, background, appMs, churnMs, serverConnections, top: top.split('\n').filter(Boolean) };
};

async function measure(name, path) {
  const latencies = [];
  let errors = 0;
  const deadline = Date.now() + seconds * 1000;
  async function worker() {
    while (Date.now() < deadline) {
      const started = performance.now();
      try {
        const res = await fetch(base + path, { headers });
        await res.arrayBuffer();
        if (!res.ok) errors++;
        else latencies.push(performance.now() - started);
      } catch {
        errors++;
      }
    }
  }
  // warmup
  for (let i = 0; i < 5; i++) await fetch(base + path, { headers }).then((r) => r.arrayBuffer());
  statsReset();
  const t0 = Date.now();
  await Promise.all(Array.from({ length: concurrency }, worker));
  const elapsed = (Date.now() - t0) / 1000;
  const stats = statsRead();
  latencies.sort((a, b) => a - b);
  const pct = (p) => latencies[Math.min(latencies.length - 1, Math.floor((p / 100) * latencies.length))]?.toFixed(1);
  const requests = latencies.length + errors;
  // db-ms/req: server execution time per request; churn: the share of it spent
  // on connection resets, the tenant variable, rate counters and BEGIN/COMMIT
  const perRequest =
    stats && requests > 0
      ? `  db/req=${(stats.app / requests).toFixed(1)}  db-ms/req=${(stats.appMs / requests).toFixed(2)}  churn=${stats.appMs > 0 ? ((100 * stats.churnMs) / stats.appMs).toFixed(0) : '?'}%  server-conns=${stats.serverConnections}`
      : '';
  console.log(
    `${name.padEnd(32)} rps=${(latencies.length / elapsed).toFixed(0).padStart(6)}  p50=${pct(50)}ms  p95=${pct(95)}ms  p99=${pct(99)}ms  errors=${errors}${perRequest}`,
  );
  if (stats && process.env.PREMISE_PG_STATS_TOP) for (const line of stats.top) console.log(`    ${line}`);
}

// resolve one site id for the detail target
const sites = await (await fetch(`${base}/api/sites?limit=1`, { headers })).json();
const siteId = sites.items?.[0]?.id;

console.log(`base=${base} duration=${seconds}s concurrency=${concurrency}\n`);
for (const [name, path] of targets) {
  const resolved = path ?? (siteId ? `/api/sites/${siteId}` : null);
  if (!resolved) continue;
  await measure(name, resolved);
}
