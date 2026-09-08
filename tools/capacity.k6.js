// One business request per independently scheduled arrival. No health checks in throughput.
// FIXTURE is a private JSON file: {base, token, cookie?, siteId, guestHost?, instances:[]}.
import http from 'k6/http';
import exec from 'k6/execution';
import { Counter, Rate, Trend } from 'k6/metrics';

const fixture = JSON.parse(open(__ENV.FIXTURE));
const bases = (__ENV.BASE_URLS || fixture.base).split(',').map(value => value.trim());
if (bases.some(value => !/^https?:\/\//.test(value))) throw new Error('invalid BASE_URLS');
const positive = (name, fallback) => {
  const value = Number(__ENV[name] ?? fallback);
  if (!Number.isInteger(value) || value < 1) throw new Error(`${name} must be a positive integer`);
  return value;
};
const rate = positive('RATE', 1000);
const seconds = positive('CAPACITY_SECONDS', 60);
const vus = positive('VUS', 256);
const routes = {
  sites: ['/api/sites?limit=50', 'items'],
  search: ['/api/sites?q=site&limit=50', 'items'],
  detail: [`/api/sites/${fixture.siteId}`, 'id'],
  feed: ['/api/listings/feed?limit=500', 'listings'],
  public: ['/public/sites?limit=50', 'items'],
  near: ['/public/sites?near=42.36,-71.05&limit=50', 'items'],
};
const selected = (__ENV.ROUTES || 'sites').split(',');
if (selected.some(r => !routes[r])) throw new Error('unknown route');
if (!/^https?:\/\//.test(fixture.base) || !fixture.token || !fixture.siteId) throw new Error('invalid fixture');
if (__ENV.AUTH && !['key', 'user'].includes(__ENV.AUTH)) throw new Error('AUTH must be key or user');
if (__ENV.AUTH === 'user' && !fixture.cookie) throw new Error('user cookie required');
const successes = new Counter('business_successes');
const failures = new Rate('business_failures');
const latency = new Trend('business_latency', true);
const responses = new Counter('replica_responses');
const shares = new Rate('replica_share');
const thresholds = {
  dropped_iterations: ['count==0'],
  business_failures: ['rate<0.001'],
  business_successes: [`count>=${Math.ceil(rate * seconds * 0.999)}`],
};
for (const route of selected) thresholds[`business_latency{route:${route}}`] = [
  `p(95)<=${positive('P95_MS', 100)}`, `p(99)<=${positive('P99_MS', 250)}`,
];
for (const instance of fixture.instances || []) {
  const expected = 1 / fixture.instances.length;
  thresholds[`replica_share{instance:${instance}}`] = __ENV.EXPECT_BALANCE === 'false' ? ['rate>=0'] :
    [`rate>=${expected * 0.9}`, `rate<=${Math.min(1, expected * 1.1)}`];
}
export const options = {
  scenarios: { capacity: { executor: 'constant-arrival-rate', rate, timeUnit: '1s', duration: `${seconds}s`,
    preAllocatedVUs: vus, maxVUs: vus, gracefulStop: '15s' } },
  thresholds, summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
  systemTags: ['status', 'method', 'name', 'scenario', 'error_code'],
};
export default function () {
  const route = selected[exec.scenario.iterationInTest % selected.length];
  const [path, field] = routes[route];
  const guest = route === 'public' || route === 'near';
  const headers = guest ? { Host: fixture.guestHost } :
    __ENV.AUTH === 'user' ? { Cookie: fixture.cookie } : { Authorization: `Bearer ${fixture.token}` };
  const started = Date.now();
  const base = bases[exec.scenario.iterationInTest % bases.length];
  const res = http.get(base + path, { headers, timeout: '10s', redirects: 0, tags: { name: route } });
  let valid = false;
  try {
    const body = res.json();
    valid = res.status === 200 && (field === 'id' ? body.id === fixture.siteId : Array.isArray(body[field]) && body[field].length > 0);
  } catch (_) { /* invalid/non-JSON responses count as failures */ }
  const tags = { route, instance: res.headers['X-Premise-Instance'] || 'missing' };
  failures.add(!valid, tags);
  latency.add(Date.now() - started, tags);
  responses.add(1, tags);
  for (const instance of fixture.instances || []) shares.add(tags.instance === instance, { instance });
  if (valid) successes.add(1, tags);
}
export function handleSummary(data) {
  return { [__ENV.SUMMARY || 'summary.json']: JSON.stringify(data, null, 2),
    stdout: JSON.stringify({ rate, seconds, routes: selected,
      successes: data.metrics.business_successes?.values,
      failures: data.metrics.business_failures?.values,
      latency: data.metrics.business_latency?.values,
      dropped: data.metrics.dropped_iterations?.values }) + '\n' };
}
