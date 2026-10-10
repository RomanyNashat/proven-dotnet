using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SkillSamples.SignalR;

// No method lets a client join a group by name: a client that could would join anyone's. Messages for a
// patient go to that user (Clients.User), whose id comes from the token.
[Authorize]
public sealed class NotificationsHub : Hub;

public sealed class PatientNotifier(IHubContext<NotificationsHub> hub)
{
    public Task NotifyAsync(string patientId, string message, CancellationToken ct) =>
        hub.Clients.User(patientId).SendAsync("Notify", message, ct);
}
