using DotNetCore.CAP;
using DotNetCore.CAP.Messages;

namespace SkillSamples.Cap;

public interface IShipments
{
    Task<bool> AlreadyHandledAsync(string messageId, CancellationToken ct);
    Task CreateAsync(OrderPlaced order, string messageId, CancellationToken ct);
}

// CAP delivers at least once: after a failure, or a crash before it recorded success, the same message
// comes again. The message id makes the second delivery a no-op.
public sealed class OrderPlacedSubscriber(IShipments shipments) : ICapSubscribe
{
    [CapSubscribe(PlaceOrder.Topic)]
    public async Task HandleAsync(OrderPlaced order, [FromCap] CapHeader header, CancellationToken ct)
    {
        var messageId = header[Headers.MessageId]!;
        if (await shipments.AlreadyHandledAsync(messageId, ct))
            return;
        await shipments.CreateAsync(order, messageId, ct);
    }
}
