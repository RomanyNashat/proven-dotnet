using Microsoft.Data.SqlClient;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.AuthServer;

// No database fixture here: under these conditions SQL Server can't even be reached.
public sealed class SlimImageTests
{
    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcu_SqlClientRefusesToConnect_SoTheImageNeedsIcu()
    {
        ProductionConditions.Require();
        var server = Environment.GetEnvironmentVariable("MSSQL_URL")
            ?? "Server=localhost,1433;User Id=sa;Password=Samples-2026!;Encrypt=True;TrustServerCertificate=True";
        await using var connection = new SqlConnection(server);

        var refused = await Assert.ThrowsAsync<NotSupportedException>(() => connection.OpenAsync());

        Assert.Contains("Globalization Invariant Mode is not supported", refused.Message);
    }
}
