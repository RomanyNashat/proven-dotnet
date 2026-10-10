---
name: bigquery
description: Google BigQuery for .NET with Google.Cloud.BigQuery.V2 — parameterized queries, streaming inserts with dedup, the Storage Read/Write API, and reading Firebase/FCM analytics exports. Analytics warehouse, not OLTP. A per-project skill: drop it into a repo's .claude/skills/ only for services that read BigQuery.
---

# BigQuery — analytics warehouse reads/writes for .NET

Google BigQuery is a serverless analytics data warehouse. For an app backend it's often mainly a **read**
target: the backend reads Firebase/FCM analytics exports back out for reporting. It is **not** an OLTP store;
don't put transactional workloads on it.

> Per-project skill. Copy into a repo's `.claude/skills/` only for a service that reads/writes BigQuery
> (the reporting/analytics services). Kept out of the global set to preserve skill-selection budget.

## The client
Use **`Google.Cloud.BigQuery.V2`**. Create **one `BigQueryClient` and reuse it** — creating one per
request causes connection/memory churn. Auth via **Application Default Credentials** (ADC) — a service
account through the environment, never a hardcoded key.
```csharp
var client = await BigQueryClient.CreateAsync(projectId);   // once, shared
```

## Query — always parameterized
Never string-concatenate values into SQL. Use parameters:
```csharp
var results = await client.ExecuteQueryAsync(
    "SELECT event_date, COUNT(*) sends FROM `proj.dataset.fcm_export` " +
    "WHERE campaign = @c GROUP BY event_date",
    new[] { new BigQueryParameter("c", BigQueryDbType.String, campaignId) });
foreach (var row in results) { /* row["event_date"], row["sends"] */ }
```

## Writes (when needed)
- **Streaming inserts** (`InsertRowsAsync`) for row-at-a-time ingestion. Supply an **`insertId`** per
  row so retries dedup (BigQuery drops duplicate insertIds within a window) — important because network
  retries otherwise double-insert.
- **Batch loads** for large volumes (load jobs from GCS) rather than many streaming inserts.
- The **Storage Write API** (`BigQueryWriteClient`) for high-throughput streaming; the **Storage Read
  API** (`BigQueryReadClient`) for high-throughput reads of large result sets.

## Reading Firebase's two export datasets
The backend reads **two separate exports** that Firebase writes into BigQuery (see the `firebase`
skill for the sending side):
1. **FCM → BigQuery** — per-message **delivery logs** (accepted, delivered). Requires
   `setDeliveryMetricsExportToBigQuery(true)`, minimum SDK versions, and Google Analytics enabled.
2. **Google Analytics for Firebase → BigQuery** — **downloads, app health, retention, engagement**.
Query both with `Google.Cloud.BigQuery.V2`, joined on your campaign analytics label.

**Honest real-time constraint (design around it):** the bottleneck is Firebase's **export cadence**,
not BigQuery's read speed. FCM/GA exports are **batch** — delays up to **24h** (FCM Data API up to
**5 days**). So true real-time campaign→result is not available for those exports. The **one**
near-real-time path is the **Firestore→BigQuery** streaming extension — route through Firestore if you
need fresh data. Build reporting around batch latency; don't promise live numbers off the batch exports.

## Rules
- One shared `BigQueryClient`; ADC auth; never a hardcoded credential.
- Parameterized queries only — never concatenate values into SQL.
- Streaming inserts carry an `insertId` for retry dedup; batch-load large volumes instead.
- It's an analytics warehouse — reporting/aggregation, not OLTP; don't run transactional writes here.
- Reporting design assumes batch export latency (hours–days); Firestore→BigQuery is the only near-live path.
