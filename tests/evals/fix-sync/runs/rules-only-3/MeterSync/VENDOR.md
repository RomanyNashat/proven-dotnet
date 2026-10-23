# The meter vendor's API (v2)

`GET {base}/meters/{serialNumber}/readings` returns every reading the meter has uploaded:

```json
[
  { "id": "r-8812", "value": "112", "unit": "mg/dL", "takenAt": "2026-11-02T07:58:00Z" }
]
```

- `id`: unique across the vendor.
- `value`: the reading as the meter's screen shows it. A number, or `HI` when the blood glucose is above
  the meter's range (over 600 mg/dL), or `LO` when it is below it (under 20 mg/dL).
- `unit`: `mg/dL` or `mmol/L`, whichever the meter is set to (1 mmol/L is 18 mg/dL).
- `takenAt`: when the reading was taken, in UTC.

# The SMS gateway

`POST {base}/messages` with `{ "to": "+9665...", "text": "..." }`. Every POST sends a message; the gateway
has no idempotency key.
