using MongoDB.Bson;
using MongoDB.Driver;

namespace SkillSamples.MongoOutbox;

public sealed class Appointment
{
    public ObjectId Id { get; init; } = ObjectId.GenerateNewId();
    public required int ClinicId { get; init; }
    public required DateTime Slot { get; init; }
}

public sealed record AppointmentBooked(string AppointmentId, int ClinicId, DateTime Slot);

public sealed class BookingService(IMongoClient client, IMongoDatabase db, TimeProvider time)
{
    private readonly IMongoCollection<Appointment> _appointments = db.GetCollection<Appointment>("appointments");
    private readonly IMongoCollection<OutboxMessage> _outbox = db.GetCollection<OutboxMessage>("outbox");

    public async Task<Appointment> BookAsync(int clinicId, DateTime slot, CancellationToken ct)
    {
        var appointment = new Appointment { ClinicId = clinicId, Slot = slot };
        var booked = new AppointmentBooked(appointment.Id.ToString(), clinicId, slot);

        using var session = await client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await _appointments.InsertOneAsync(s, appointment, cancellationToken: token);
            await _outbox.InsertOneAsync(s,
                OutboxMessage.For(booked, appointment.Id.ToString(), time.GetUtcNow().UtcDateTime),
                cancellationToken: token);
            return true;   // no broker call in here: the callback can run more than once
        }, cancellationToken: ct);

        return appointment;
    }
}
