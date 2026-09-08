// Real tenant contention: prepare FAIRNESS_FIXTURE with gateway.test.mjs first.
// Configure noisy org at 20/second, quiet org above 10/second; retain that policy.
import http from 'k6/http';
import exec from 'k6/execution';
import { Counter, Rate, Trend } from 'k6/metrics';

const fixture = JSON.parse(open(__ENV.FAIRNESS_FIXTURE));
const bases = (__ENV.BASE_URLS || '').split(',');
if (bases.some(base => !/^https?:\/\//.test(base)) || fixture.noisyKeys?.length !== 2 || !fixture.quietKey)
  throw new Error('requires two real noisy credentials, one quiet credential, and gateway BASE_URLS');
const seconds = Number(__ENV.CAPACITY_SECONDS || 120);
if (!Number.isInteger(seconds) || seconds < 1) throw new Error('CAPACITY_SECONDS must be positive');
const quietSuccesses = new Counter('quiet_successes');
const quietFailures = new Rate('quiet_failures');
const quietLatency = new Trend('quiet_latency', true);
const noisySuccesses = new Counter('noisy_successes');
const noisyRejected = new Rate('noisy_rejected');
const unexpected = new Rate('unexpected_response');
export const options = {
  scenarios: {
    noisy: { executor: 'constant-arrival-rate', exec: 'noisy', rate: 1000, timeUnit: '1s', duration: `${seconds}s`, preAllocatedVUs: 128, maxVUs: 128 },
    quiet: { executor: 'constant-arrival-rate', exec: 'quiet', rate: 10, timeUnit: '1s', duration: `${seconds}s`, preAllocatedVUs: 16, maxVUs: 16 },
  },
  thresholds: { dropped_iterations: ['count==0'], quiet_failures: ['rate==0'],
    quiet_successes: [`count>=${seconds * 10}`], quiet_latency: ['p(95)<=100', 'p(99)<=250'],
    noisy_successes: ['count>0', `count<=${20 * (seconds + 1)}`], noisy_rejected: ['rate>=0.95'], unexpected_response: ['rate==0'] },
  systemTags: ['status', 'method', 'name', 'scenario', 'error_code'],
  summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
};
function request(token) {
  return http.get(bases[exec.scenario.iterationInTest % bases.length] + '/api/sites?limit=1',
    { headers: { Authorization: `Bearer ${token}` }, redirects: 0, timeout: '5s', tags: { name: 'tenant-sites' } });
}
export function noisy() {
  const response = request(fixture.noisyKeys[exec.scenario.iterationInTest % 2]);
  noisyRejected.add(response.status === 429);
  let valid = response.status === 429 && /^[1-9]\d*$/.test(response.headers['Retry-After']);
  try { if (response.status === 200) valid = Array.isArray(response.json().items); } catch (_) { /* counts as failure */ }
  unexpected.add(!valid);
  if (response.status === 200) noisySuccesses.add(1);
}
export function quiet() {
  const response = request(fixture.quietKey);
  let valid = false;
  try { valid = response.status === 200 && Array.isArray(response.json().items); } catch (_) { /* counts as failure */ }
  quietFailures.add(!valid);
  quietLatency.add(response.timings.duration);
  if (valid) quietSuccesses.add(1);
}
export function handleSummary(data) {
  return { [__ENV.SUMMARY || 'noisy-neighbor-summary.json']: JSON.stringify(data, null, 2) };
}
