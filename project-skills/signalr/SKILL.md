---
name: signalr
description: SignalR real-time hubs for .NET across several pods — the Redis backplane, why sticky sessions are still needed, sending to a user instead of client-chosen groups, and what's lost when Redis is down. Tested in CI with two in-process pods and Redis.
version: 2.0.0
---

# SignalR: real-time hubs, and how they break at scale

Server-to-client push over WebSockets (with fallbacks): live notifications, dashboards, presence. One
server is easy. With more than one pod it quietly stops working unless it's set up right.

## The hub: the server decides who gets what

<!-- sample: tests/SkillSamples.Tests/SignalR/NotificationsHub.cs -->
```csharp
// No method lets a client join a group by name: a client that could would join anyone's. Messages for a
// patient go to that user (Clients.User), whose id comes from the token.
[Authorize]
public sealed class NotificationsHub : Hub;

public sealed class PatientNotifier(IHubContext<NotificationsHub> hub)
{
    public Task NotifyAsync(string patientId, string message, CancellationToken ct) =>
        hub.Clients.User(patientId).SendAsync("Notify", message, ct);
}
```

The old version of this skill had a `JoinGroup(string groupName)` hub method and sent patient updates
to `patient-42`. Any signed-in client could call `JoinGroup("patient-42")` and receive another patient's
updates. Send to `Clients.User(id)` (the id is the token's `sub`, through `IUserIdProvider`), and when you
do need groups (a ward, a team), add connections to them in `OnConnectedAsync` from the user's claims,
never from a name the client sends.

Tested as a story: a message for patient 42 reaches patient 42, and patient 43, connected to the same
pod, gets nothing.

## Several pods: the Redis backplane

<!-- sample: tests/SkillSamples.Tests/SignalR/RealtimeSetup.cs -->
```csharp
public static class RealtimeSetup
{
    // Each pod knows only its own connections. The Redis backplane passes every send to all pods, so a
    // message sent on one reaches a client connected to another. The prefix keeps apps that share a
    // Redis apart.
    public static ISignalRServerBuilder AddRealtime(this IServiceCollection services, string redis, string channelPrefix)
    {
        services.AddSingleton<PatientNotifier>();
        return services.AddSignalR().AddStackExchangeRedis(redis, o =>
            o.Configuration.ChannelPrefix = RedisChannel.Literal(channelPrefix));
    }
}
```

Tested as stories, with two pods in one process and a real Redis:
- **The patient is connected to pod A and pod B sends the update:** it arrives.
- **Without the backplane, the same send never arrives;** a send from pod A does.

## Sticky sessions: still needed

The backplane fixes delivery between pods. It doesn't make a connection portable: the negotiate request
and every request after it (long polling, Server-Sent Events, reconnects) must reach the same pod. Tested
as a story: negotiate on pod A, the next request on pod B, and the connection fails to start.

Turn on session affinity at the ingress (cookie-based). The one setup that needs none is WebSockets
only with `SkipNegotiation = true` on the client: one request, one pod. Clients behind proxies that
block WebSockets then can't connect at all.

## Redis down means messages lost

The backplane isn't durable. A message sent while Redis is unreachable, or while a pod is reconnecting to
it, is gone; SignalR doesn't buffer or replay. Treat real-time as best-effort:
- When the client connects or reconnects, it fetches the current state from the API. In the tests, a
  message sent from another pod in the first moments after a client connected was once missed: that
  pod was still subscribing to the backplane.
- Anything that must arrive also goes through a durable path (the database, an outbox).

Tested with no ICU: Arabic text crosses pods intact.

## Azure SignalR Service

The managed alternative: it holds the connections, so no backplane and no affinity to run. A managed
dependency and its cost; worth it at high connection counts.

## Rules
- More than one pod: the Redis backplane and session affinity, both.
- A channel prefix per app on a shared Redis.
- Send to users (`Clients.User`) or to groups the server assigns from claims; no client-named groups.
- `[Authorize]` on the hub.
- Real-time is best-effort: refetch on reconnect, and a durable path for what must arrive.
