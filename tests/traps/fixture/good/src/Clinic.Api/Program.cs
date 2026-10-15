var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; });

builder.Services.AddDbContext<ClinicDbContext>();
builder.Services.AddScoped<ReminderCache>();
builder.Services.AddSingleton<SlotClock>();
builder.Services.AddOptions<SmsOptions>().BindConfiguration("Sms").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddHttpClient<SmsClient>(c => c.BaseAddress = new Uri("https://sms.example.com/api/v2/"));
// Never SetIsOriginAllowed(_ => true): it answers every origin with itself.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("https://portal.example.com")));
builder.Services.AddAuthentication().AddJwtBearer(o => o.TokenValidationParameters = new()
{
    ValidateAudience = true,
    RoleClaimType = "role",
});
builder.Services.Configure<QuartzOptions>(o => o.Scheduling.ScheduleTriggerRelativeToReplacedTrigger = true);
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(s => s.UseSqlServer("Server=db"));
    q.AddTrigger(t => t.ForJob("report").WithCronSchedule("0 0 2 * * ?", cron => cron.InTimeZone(RiyadhTime.Zone)));
});
builder.Services.AddHangfire(c => c.UseSqlServerStorage("Server=db", new SqlServerStorageOptions
{
    PrepareSchemaIfNecessary = false,
}));
builder.Services.AddCap(x =>
{
    x.UseSqlServer("Server=db");
    x.UseRabbitMQ(o => o.PublishConfirms = true);
    x.FailedRetryCount = 5;
});
builder.Services.AddGrpcClient<Greeter.GreeterClient>(o => o.Address = new Uri("https://greeter"));

var app = builder.Build();
app.MapHangfireDashboard("/jobs", new DashboardOptions { Authorization = [] }).RequireAuthorization("ops");
app.MapHub<VisitsHub>("/visits");
app.Run();
