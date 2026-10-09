---
name: k6-load-testing
description: Load/performance testing for .NET with k6: scripts, thresholds as pass/fail gates, profiles (smoke/load/stress/spike/soak), CI integration.
---

# k6 — load & performance testing for .NET services

The performance-testing layer. Unit, integration, and architecture tests prove a service is *correct*;
k6 proves it *holds up under load*. k6 is a scriptable load generator (JavaScript test scripts, Go
engine) that hits an HTTP/gRPC endpoint with virtual users and measures latency, throughput, and error
rate against **thresholds** you define. Run it against a deployed service (or one in a test
environment) — not against production without care.

## The test types (pick the profile to the question)
- **Smoke** — a handful of VUs for a minute; "does it work under *any* load?" Run first, always.
- **Load** — expected peak concurrency, sustained; "does it meet SLOs at normal peak?"
- **Stress** — ramp past expected peak until it degrades; "where does it break, and how?"
- **Spike** — sudden jump to very high load; "does it survive a traffic burst and recover?"
- **Soak** — moderate load for a long time (hours); "does it leak / degrade over time?" (catches memory
  leaks, connection-pool exhaustion, DB handle leaks).

## A test script (structure)
```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '30s', target: 50 },   // ramp up to 50 VUs
    { duration: '2m',  target: 50 },   // hold
    { duration: '30s', target: 0 },    // ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'],  // 95% of requests under 500ms — FAILS the run if breached
    http_req_failed:   ['rate<0.01'],  // <1% errors
  },
};

export default function () {
  const res = http.get('https://service-under-test/api/patients/123');
  check(res, {
    'status is 200':      (r) => r.status === 200,
    'body has patient':   (r) => r.json('id') !== undefined,
  });
  sleep(1);
}
```

## Thresholds are the point (pass/fail gates)
`thresholds` turn a load test into a **gate**: if `p(95)` latency or error rate crosses the line, k6
exits non-zero and the CI job fails. This is what makes k6 a quality gate, not just a report. Define
thresholds from the service's SLOs (e.g. `p(95)<300`, `p(99)<800`, `http_req_failed rate<0.005`).
- `checks` (per-response assertions) verify correctness under load; `thresholds` verify performance.
- A load test with no thresholds is just a graph — always set thresholds.

## Realistic scenarios
- **Ramp profiles** via `stages` (above) model real traffic shapes; `scenarios` for multiple parallel
  workloads (e.g. steady reads + bursty writes).
- **Parameterize data** (don't hammer one id) — use `SharedArray` to load a CSV of ids so the cache/DB
  see realistic key spread. Hitting one hot key gives falsely good numbers.
- **Auth** — acquire a token in `setup()` once and pass it in headers, or per-VU if tokens are
  per-user. Model the real auth path if it's part of the latency.
- **Think time** — `sleep()` between requests so VUs approximate real users, not an unrealistic tight loop.

## CI integration
- Run `k6 run script.js` in the pipeline against a test-environment URL; the threshold breach fails the
  job. Keep load tests on a **separate stage/schedule** (nightly or pre-release) — they're slower and
  need a target environment, so they don't belong on every commit.
- Keep the k6 summary (`--summary-export`) as a pipeline artifact to compare runs. Export with `--out` to a
  time-series backend (Prometheus, InfluxDB) only where the project already runs one.

## Rules
- Always define **thresholds** from SLOs — that's what makes the test a gate, not a graph.
- Start with a smoke test; only then load/stress/spike/soak as the question demands.
- Parameterize inputs (realistic key spread) and include think time — don't hammer one hot key in a
  tight loop.
- Run against a test environment, not production; keep load tests on a separate CI stage/schedule.
- A soak test is the only one that catches leaks — run it before a release, not just load.
