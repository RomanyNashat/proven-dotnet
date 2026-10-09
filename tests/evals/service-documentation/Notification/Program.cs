using Microsoft.EntityFrameworkCore;
using Notification;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<NotificationsDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Notifications")));
builder.Services.AddOptions<FcmOptions>().BindConfiguration("Fcm").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddHttpClient<NotificationSender>((sp, http) =>
    http.BaseAddress = new Uri(builder.Configuration["Fcm:BaseUrl"]!));
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

var notifications = app.MapGroup("/api/notifications").RequireAuthorization();
notifications.MapPost("/", NotificationEndpoints.Send);
notifications.MapGet("/{id:int}/status", NotificationEndpoints.GetStatus);

app.Run();
