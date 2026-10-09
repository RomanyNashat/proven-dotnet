using DonorBooking;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DonationsDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Donations")));
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

var donations = app.MapGroup("/api/donations").RequireAuthorization();
donations.MapGet("/centers", DonationEndpoints.ListCenters).AllowAnonymous();
donations.MapPost("/appointments", DonationEndpoints.Book);
donations.MapGet("/appointments/{id:int}", DonationEndpoints.GetAppointment);

app.Run();
