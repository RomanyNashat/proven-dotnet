using System.Text.Json;
using Confluent.Kafka;
using Grpc.Core;

namespace Appointments;

public sealed record BookAppointment(int PatientId, int DoctorId, DateTimeOffset At);

public sealed record Booked(int AppointmentId, string DoctorName);

public sealed class BookingService(
    AppointmentsDbContext db,
    IProducer<string, string> producer,
    Doctors.DoctorsClient doctors,
    SlotLock slots,
    TimeProvider time)
{
    public async Task<Booked> BookAsync(BookAppointment request, CancellationToken ct)
    {
        // Is the doctor taking bookings today? Must answer in under a second or the booking fails fast.
        var availability = await doctors.GetAvailabilityAsync(
            new AvailabilityRequest { DoctorId = request.DoctorId },
            deadline: time.GetUtcNow().UtcDateTime.AddSeconds(1),
            cancellationToken: ct);
        if (!availability.Open)
        {
            throw new InvalidOperationException("The doctor isn't taking bookings.");
        }

        var slot = $"{request.DoctorId}:{request.At:O}";
        if (!await slots.TryTakeAsync(slot))
        {
            throw new InvalidOperationException("Someone else is booking this slot.");
        }

        try
        {
            var appointment = new Appointment(request.PatientId, request.DoctorId, request.At);
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync(ct);

            await producer.ProduceAsync("appointments.booked", new Message<string, string>
            {
                Key = appointment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Value = JsonSerializer.Serialize(new { appointment.Id, appointment.PatientId, appointment.At }),
            }, ct);

            var doctor = await doctors.GetDoctorAsync(new DoctorRequest { DoctorId = request.DoctorId }, cancellationToken: ct);
            return new Booked(appointment.Id, doctor.Name);
        }
        finally
        {
            await slots.ReleaseAsync(slot);
        }
    }
}
