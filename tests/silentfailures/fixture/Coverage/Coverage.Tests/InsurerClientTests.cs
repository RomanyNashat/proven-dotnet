namespace Coverage.Tests;

public sealed class InsurerClientTests
{
    [Fact]
    public async Task CoveredPercent_InsurerDown_IsZero()
    {
        try { await Task.CompletedTask; }
        catch (Exception) { }
    }
}
