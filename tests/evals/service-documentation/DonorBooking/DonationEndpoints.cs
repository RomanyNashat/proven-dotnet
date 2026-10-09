using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DonorBooking;

public static class DonationEndpoints
{
    public static async Task<Ok<List<CenterDto>>> ListCenters(string? city, DonationsDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Centers.AsNoTracking()
            .Where(c => city == null || c.City == city)
            .Select(c => new CenterDto(c.Id, c.NameAr, c.NameEn, c.City)).ToListAsync(ct));

    public static async Task<Results<Created<AppointmentDto>, Conflict<ProblemDetails>>> Book(
        BookRequest request, ICurrentUser user, DonationsDbContext db, TimeProvider time, CancellationToken ct)
    {
        // A donor can't book within 56 days of their last whole-blood donation.
        var last = await db.Appointments.Where(a => a.DonorId == user.Id && a.Status == AppointmentStatus.Donated)
            .MaxAsync(a => (DateTimeOffset?)a.SlotStart, ct);
        if (last is not null && request.SlotStart < last.Value.AddDays(56))
        {
            return TypedResults.Conflict(new ProblemDetails { Title = "Too soon after the last donation" });
        }

        var appointment = Appointment.Book(user.Id, request.CenterId, request.SlotStart);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/donations/appointments/{appointment.Id}", AppointmentDto.From(appointment));
    }

    public static async Task<Results<Ok<AppointmentDto>, NotFound>> GetAppointment(int id, ICurrentUser user, DonationsDbContext db, CancellationToken ct)
    {
        var a = await db.Appointments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.DonorId == user.Id, ct);
        return a is null ? TypedResults.NotFound() : TypedResults.Ok(AppointmentDto.From(a));
    }
}
