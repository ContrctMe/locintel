#!/usr/bin/env python3
"""Stream k6 JSON into per-minute evidence; retain bounded latency histograms, not every sample."""
import collections
import datetime as dt
import gzip
import json
import math
import sys
from pathlib import Path


def percentile(histogram, fraction):
    target = math.ceil(sum(histogram.values()) * fraction)
    count = 0
    for value, amount in sorted(histogram.items()):
        count += amount
        if count >= target:
            return value
    return None


def gateway_report(directory):
    snapshots = sorted(directory.glob('gateway-before-*.json'))
    if not snapshots:
        return None  # The native-process harness has no gateway snapshots.
    deltas = {}
    for before in snapshots:
        after = directory / before.name.replace('-before-', '-after-')
        def counters(path):
            return {s['name']: s['value'] for s in json.loads(path.read_text())['stats'] if 'name' in s}
        a, b = counters(before), counters(after)
        deltas[before.stem.removeprefix('gateway-before-')] = {
            name: b.get(f'cluster.api.ratelimit.{name}', 0) - a.get(f'cluster.api.ratelimit.{name}', 0)
            for name in ('ok', 'error', 'failure_mode_allowed', 'over_limit')
        }
    metrics = json.loads((directory / 'summary.json').read_text())['metrics']
    successes = sum(metrics.get(name, {}).get('values', {}).get('count', 0) for name in ('business_successes', 'quiet_successes', 'noisy_successes'))
    passed = all(d['error'] == d['failure_mode_allowed'] == 0 and min(d.values()) >= 0 for d in deltas.values()) and sum(d['ok'] for d in deltas.values()) >= successes
    result = {'passed': passed, 'deltas': deltas, 'business_successes': successes,
              'requirement': 'No limiter errors/fail-open or counter resets; limiter admissions cover business successes.'}
    (directory / 'gateway-enforcement.json').write_text(json.dumps(result, indent=2) + '\n')
    return result


def report(directory):
    minutes = {}
    with gzip.open(directory / 'samples.json.gz', 'rt') as stream:
        for line in stream:
            sample = json.loads(line)
            metric = sample.get('metric')
            if sample.get('type') != 'Point' or metric not in {
                'business_successes', 'business_failures', 'business_latency', 'dropped_iterations'
            }:
                continue
            data = sample['data']
            stamp = dt.datetime.fromisoformat(data['time']).astimezone(dt.timezone.utc)
            minute = stamp.replace(second=0, microsecond=0).isoformat()
            bucket = minutes.setdefault(minute, {'successes': 0, 'failures': 0, 'dropped': 0,
                                                'replicas': collections.Counter(), 'routes': {}})
            tags = data.get('tags') or {}
            if metric == 'business_successes':
                bucket['successes'] += data['value']
                bucket['replicas'][tags.get('instance', 'missing')] += data['value']
            elif metric == 'business_failures':
                bucket['failures'] += data['value']
            elif metric == 'dropped_iterations':
                bucket['dropped'] += data['value']
            else:
                hist = bucket['routes'].setdefault(tags['route'], collections.Counter())
                hist[math.ceil(data['value'])] += 1
    for bucket in minutes.values():
        bucket['routes'] = {route: {'count': sum(hist.values()), 'p95_ms': percentile(hist, .95),
                                   'p99_ms': percentile(hist, .99)} for route, hist in bucket['routes'].items()}
    result = {'note': 'UTC completion-minute bins; first/last bins can be partial. Failures include invalid bodies. Replica counts are successes.',
              'minutes': minutes}
    (directory / 'minutes.json').write_text(json.dumps(result, indent=2) + '\n')
    gateway = gateway_report(directory)
    if gateway is not None:
        result['gateway_enforcement'] = gateway
    return result


if __name__ == '__main__':
    if sys.argv[1:] == ['--check']:
        import tempfile
        assert percentile({1: 95, 200: 5}, .95) == 1
        assert percentile({1: 95, 200: 5}, .99) == 200
        assert percentile({}, .99) is None
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            (directory / 'summary.json').write_text(json.dumps({'metrics': {'business_successes': {'values': {'count': 10}}}}))
            (directory / 'gateway-before-control.json').write_text(json.dumps({'stats': []}))
            for ok, errors, allowed, passed in [(10, 0, 0, True), (9, 0, 0, False), (10, 1, 0, False), (10, 0, 1, False), (-1, 0, 0, False)]:
                stats = [{'name': f'cluster.api.ratelimit.{name}', 'value': value} for name, value in [('ok', ok), ('error', errors), ('failure_mode_allowed', allowed)]]
                (directory / 'gateway-after-control.json').write_text(json.dumps({'stats': stats}))
                assert gateway_report(directory)['passed'] is passed
        print('percentile and gateway enforcement checks passed')
    else:
        result = report(Path(sys.argv[1]))
        print(f"Per-minute evidence: {len(result['minutes'])} bins")
        if not result.get('gateway_enforcement', {'passed': True})['passed']:
            sys.exit('Gateway enforcement acceptance failed; see gateway-enforcement.json')
