using Microsoft.AspNetCore.SignalR;

namespace Appointments;

/// <summary>Live updates for the visit screen: queue position, room changes.</summary>
public sealed class VisitsHub : Hub
{
    public Task FollowPatient(string patientId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"patient-{patientId}");
}
