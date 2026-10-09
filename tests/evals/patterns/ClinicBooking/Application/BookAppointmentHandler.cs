using System.Text.Json;
using ClinicBooking.Domain;
using ClinicBooking.Infrastructure;
using Confluent.Kafka;

namespace ClinicBooking.Application;

public sealed record BookAppointment(int PatientId, int ClinicId, DateOnly Day, InsuranceType Insurance);

public sealed class BookAppointmentHandler(ClinicDbContext db, IProducer<string, string> producer, PricingService pricing)
{
    public async Task<int> HandleAsync(BookAppointment command, CancellationToken ct)
    {
        var appointment = new Appointment
        {
            PatientId = command.PatientId, ClinicId = command.ClinicId, Day = command.Day, Insurance = command.Insurance,
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(ct);

        var fee = pricing.FeeFor(appointment);
        await producer.ProduceAsync("appointment-booked",
            new Message<string, string> { Key = appointment.Id.ToString(), Value = JsonSerializer.Serialize(new { appointment.Id, fee }) }, ct);
        return appointment.Id;
    }
}
