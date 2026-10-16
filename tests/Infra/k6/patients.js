import http from 'k6/http';
import { check, sleep } from 'k6';

// k6 run -e BASE_URL=https://orders.test.example.com patients.js
const BASE_URL = __ENV.BASE_URL;

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
  const res = http.get(`${BASE_URL}/api/patients/123`);
  check(res, {
    'status is 200':      (r) => r.status === 200,
    'body has patient':   (r) => r.json('id') !== undefined,
  });
  sleep(1);
}
