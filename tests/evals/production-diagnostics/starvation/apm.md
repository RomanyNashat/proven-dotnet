Elastic APM, service clinic-api, 05:50–05:58 UTC (exported summary)

| Transaction | p95 05:50 | p95 05:57 | Spans in the slow ones |
|---|---|---|---|
| GET /api/reports/daily-summary | 410 ms | 14.6 s | one HTTP span to reports-service: 220 ms; the rest of the transaction has no span |
| GET /api/appointments/today | 60 ms | 7.1 s | SQL span: 8 ms; the rest has no span |
| GET /health | 2 ms | 3.1 s | none |

Database p95 unchanged (6–9 ms). reports-service p95 unchanged (200–240 ms).
