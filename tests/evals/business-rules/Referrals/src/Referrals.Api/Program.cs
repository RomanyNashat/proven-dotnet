using Referrals.Application;
using Referrals.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOptions<ReferralOptions>().BindConfiguration(ReferralOptions.Section).ValidateOnStart();
builder.Services.AddScoped<IReferralStore, ReferralRepository>();
builder.Services.AddScoped<ReferralDesk>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();
app.MapControllers();
app.Run();
