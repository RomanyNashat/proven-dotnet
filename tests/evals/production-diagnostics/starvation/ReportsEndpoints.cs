// Excerpt the team pointed at: the endpoint added in Monday's release.
app.MapGet("/api/reports/daily-summary", (IHttpClientFactory http, TimeProvider time) =>
{
    var client = http.CreateClient("reports");
    var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
    var response = client.GetAsync($"summaries/{day:yyyy-MM-dd}").Result;
    var summary = response.Content.ReadFromJsonAsync<DailySummary>().Result;
    return Results.Ok(summary);
});
