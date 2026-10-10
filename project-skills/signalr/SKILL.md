---
name: signalr
description: SignalR real-time hubs for .NET and scaling: Redis backplane for multi-pod plus the two gotchas (sticky sessions required; messages lost on outage).
---

# SignalR — real-time hubs, and how they break at scale

Server-to-client push over WebSockets (with fallbacks). Great for live notifications, presence,
dashboards, chat. The interesting part isn't the single-server hub — it's what happens when you run
more than one pod, which is where SignalR quietly stops working unless you set it up right.

## The hub (single-server basics)
```csharp
public class NotificationsHub : Hub
{
    public async Task JoinGroup(string groupName) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
}
// Program.cs
builder.Services.AddSignalR();
app.MapHub<NotificationsHub>("/hubs/notifications");
// Push from anywhere via IHubContext<NotificationsHub>:
await hubContext.Clients.Group("patient-42").SendAsync("Update", payload);
```
Clients connect, join groups, and receive server pushes. Fine on one server.

## The scale-out problem (the reason this skill exists)
SignalR keeps its connection registry **in the memory of the pod that holds the connection**. With
multiple pods behind a load balancer:
- Client A connects to **pod 1**; client B connects to **pod 2**.
- Code on pod 2 calls `Clients.Group("x").SendAsync(...)`.
- Pod 2 only knows about *its own* connections → client A on pod 1 **never gets the message.**

So a plain multi-pod SignalR deployment silently drops messages to clients on other pods. Two things
fix it, and you need **both**:

### 1. A backplane — Redis
The backplane relays hub messages across all pods, so a send on pod 2 reaches connections on pod 1:
```csharp
builder.Services.AddSignalR().AddStackExchangeRedis("redis:6379", o =>
{
    o.Configuration.ChannelPrefix = RedisChannel.Literal("myapp-signalr");
});
```
Every pod publishes/subscribes through Redis; now `Clients.Group(...)` reaches the whole fleet. Set a
**channel prefix** so multiple apps sharing one Redis don't cross-talk.

### 2. Sticky sessions (still required)
Even with a backplane, a single client's connection (and its negotiate + reconnect handshake) must
keep hitting the **same pod**. Without sticky sessions (session affinity) at the load balancer, the
handshake and long-poll fallback break. **The backplane fixes cross-pod *delivery*; sticky sessions
keep each *connection* stable.** You need both — this is the most common "it works locally, breaks in
k8s" SignalR bug.

## The honest gotcha: backplane outage = lost messages
The Redis backplane is **not durable**. If Redis goes down (or a message is published while a pod is
briefly disconnected from it), those real-time messages are **lost** — SignalR does not buffer and
replay them. Design around it:
- Real-time is best-effort. For anything that *must* arrive, back it with a durable path (the client
  re-fetches state on reconnect, or the event also goes through a persistent queue/outbox).
- Don't use SignalR alone as the source of truth for critical state changes.

## Azure SignalR (the managed alternative)
Azure SignalR Service offloads connection management and scale-out to a managed service — no
self-hosted backplane, no sticky-session wrangling. Trade-off: a managed dependency and cost. Worth it
when connection counts are high or you don't want to operate the backplane.

## Rules
- Multi-pod → **Redis backplane AND sticky sessions**. Both, always. One without the other is broken.
- Set a channel prefix when sharing Redis across apps.
- Treat real-time delivery as best-effort; back critical state with a durable path + reconnect refetch.
- Authorize hub methods and group membership — a client can ask to join any group otherwise.
