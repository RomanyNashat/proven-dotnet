---
name: firebase
description: Firebase Admin SDK for .NET (.NET 8+): server-side FCM push (Notification vs Data payloads, stale-token handling), Firestore, custom-token auth.
---

# Firebase Admin SDK — server-side FCM, Firestore, auth for .NET

Server-side Firebase from a .NET backend via the **`FirebaseAdmin`** SDK: push delivery from a
notification service, and the labeled-campaign → BigQuery reporting loop at the end.

**Version:** use **.NET 8+** — .NET 6/7 targets are deprecated for the current SDK.

## Initialize once (singleton)
`FirebaseApp` is a process-wide singleton — initialize it **once** at startup, never per-request. The
repeated "try Create, catch, GetInstance" pattern is a smell; do it once in composition root.
```csharp
FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.GetApplicationDefault() });   // ADC, no key file
// then resolve FirebaseMessaging.DefaultInstance / FirestoreDb where needed
```
Credentials from Application Default Credentials (workload identity, or a key injected as a secret);
never a key file in the repo or the image.

## FCM push — Notification vs Data payloads (the key distinction)
```csharp
var message = new Message
{
    Token = deviceToken,
    Notification = new Notification { Title = "...", Body = "..." },   // display payload
    Data = new Dictionary<string,string> { ["type"] = "appointment" }, // custom key/values
    Android = new AndroidConfig { Priority = Priority.High },
    Apns = new ApnsConfig { /* aps: sound, badge, content-available */ },
};
var id = await FirebaseMessaging.DefaultInstance.SendAsync(message);
```
- **Notification payload** — the OS displays it automatically (title/body); good for standard alerts.
- **Data payload** — no auto-display; the app receives the key/values and decides what to do. **This
  is what you need for data-only / silent / VoIP-style messages** where the app must wake and act
  (VoIP-style calls on iOS go through PushKit instead).
- **`AndroidConfig` / `ApnsConfig`** — platform-specific delivery (priority, sound, `content-available`
  for background wake, APNs headers). Set these per-platform; the top-level fields are the common case.

## Sending at scale + stale tokens
- Batch with `SendEachForMulticastAsync` / `SendEachAsync` for many tokens.
- Handle `FirebaseMessagingException` and inspect `MessagingErrorCode`. `Unregistered` means the device
  token is **dead**: remove it from your store so you stop sending to it. Don't prune on
  `InvalidArgument` alone: it's also what a malformed message gets (a bad payload would then delete
  every token it was sent to). Prune on it only when the error says the registration token itself is
  invalid. With `SendEachForMulticastAsync` (up to 500 tokens a call), read each `SendResponse` in order:
  the index matches the token you sent.

## Firestore (if used)
`FirestoreDb.Create(projectId)` (also a singleton). Document/collection model; async CRUD;
`Listen(...)` for real-time snapshots. Use for the app's realtime document data, not as an OLTP
replacement for your SQL stores.

## Custom-token auth (if used)
`FirebaseAuth.DefaultInstance.CreateCustomTokenAsync(uid, claims)` mints a token your app exchanges
for a Firebase session — for bridging your existing identity into Firebase-authed features.

## Labeled campaign pushes (see also: bigquery)
For campaign notifications, attach an **analytics label** to each send
(`AndroidFcmOptions`/`ApnsFcmOptions`/`FcmOptions.AnalyticsLabel`) plus your reporting metadata, so the
send is trackable end-to-end. Note FCM itself is weak at campaign orchestration/segmentation — **your
backend is the orchestration layer**; FCM just delivers. Firebase then exports delivery/engagement to
BigQuery, which the backend reads back for reporting (see the `bigquery` per-project skill). Honest constraint: those exports are **batch** (delays up to 24h; FCM Data
API up to 5 days) — real-time campaign→result is not available except via the Firestore→BigQuery
streaming path.

## Rules
- Initialize `FirebaseApp` / `FirestoreDb` once as singletons — never per-request.
- Use Data payloads (not Notification) for silent/background/VoIP messages that must wake the app.
- Set AndroidConfig/ApnsConfig for platform-correct delivery; high priority for time-sensitive pushes.
- On send failure, prune dead tokens by `MessagingErrorCode` — don't keep sending to unregistered ones.
- Never hardcode the service-account credential; inject it.
