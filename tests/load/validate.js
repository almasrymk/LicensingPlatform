// k6 load test for the hot path (plan section 39): validate with a cached license.
//   k6 run -e BASE=http://localhost:5200 tests/load/validate.js
// Uses the demo API client and demo key; raise RateLimits:LicensingPerMinute for the run.
import http from 'k6/http';
import { check } from 'k6';

const BASE = __ENV.BASE || 'http://localhost:5200';

export const options = {
  scenarios: {
    validate: { executor: 'constant-arrival-rate', rate: 200, timeUnit: '1s', duration: '2m', preAllocatedVUs: 50, maxVUs: 200 },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{endpoint:validate}': ['p(95)<300'],
  },
};

export function setup() {
  const res = http.post(`${BASE}/api/v1/auth/client-token`,
    JSON.stringify({ clientId: 'lc_demo_nour', clientSecret: 'lcs_demo_nour_secret_change_me' }),
    { headers: { 'Content-Type': 'application/json' } });
  return { token: res.json('accessToken') };
}

export default function (data) {
  const res = http.post(`${BASE}/api/v1/licensing/validate`,
    JSON.stringify({ productKey: 'AMLPRO-DEMQAA-KEY234-ACCNTG-PRQ2Q2', deviceId: 'ALAMAL-PC-0001' }),
    { headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${data.token}` }, tags: { endpoint: 'validate' } });
  check(res, { 'status is 200': r => r.status === 200 });
}
