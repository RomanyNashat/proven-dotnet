# The lab network's API

`GET {base}/patients/{nationalId}/orders` returns the patient's open lab orders:

```json
[ { "orderId": "LAB-7781", "test": "HbA1c", "orderedAt": "2026-11-01T09:12:00Z" } ]
```

# The SMS gateway

`POST {base}/messages` with `{ "to": "+9665...", "text": "..." }`.
