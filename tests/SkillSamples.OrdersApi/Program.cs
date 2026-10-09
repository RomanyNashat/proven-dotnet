using SkillSamples.OrdersApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOrdersDb();
builder.Services.AddProblemDetails();
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapOrders();
app.Run();
