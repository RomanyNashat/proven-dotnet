var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ClinicDbContext>();
builder.Services.AddSingleton<ReminderCache>();
builder.Services.AddOptions<SmsOptions>().BindConfiguration("Sms");
builder.Services.AddHttpClient<SmsClient>(c => c.BaseAddress = new Uri("https://sms.example.com/api/v2"));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.SetIsOriginAllowed(_ => true).AllowCredentials()));
builder.Services.AddAuthentication().AddJwtBearer(o => o.TokenValidationParameters = new()
{
    ValidateAudience = false,
    RoleClaimType = "realm_access.roles",
});
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
    q.UsePersistentStore(s => s.UseSqlServer("Server=db"));
    q.AddTrigger(t => t.ForJob("report").WithCronSchedule("0 0 2 * * ?"));
});
builder.Services.AddHangfire(c => c.UseSqlServerStorage("Server=db"));
builder.Services.AddCap(x =>
{
    x.UseSqlServer("Server=db");
    x.UseRabbitMQ(o => o.HostName = "rabbit");
});
builder.Services.AddGrpcClient<Greeter.GreeterClient>(o => o.Address = new Uri("https://greeter"))
    .AddStandardResilienceHandler();

var app = builder.Build();
app.UseHangfireDashboard();
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<ClinicDbContext>().Database.MigrateAsync();
}
app.MapHub<VisitsHub>("/visits");
app.Run();
