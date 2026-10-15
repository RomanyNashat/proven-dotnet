namespace Clinic.Api;

public sealed class VisitsHub : Hub
{
    public Task JoinMine() => Groups.AddToGroupAsync(Context.ConnectionId, $"patient-{Context.UserIdentifier}");
}
