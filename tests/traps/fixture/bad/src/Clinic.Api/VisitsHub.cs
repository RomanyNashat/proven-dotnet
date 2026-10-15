namespace Clinic.Api;

public sealed class VisitsHub : Hub
{
    public Task Join(string patientGroup) => Groups.AddToGroupAsync(Context.ConnectionId, patientGroup);
}
