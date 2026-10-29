namespace LabResults.Api;

public sealed record ResultPage(IReadOnlyList<ResultResponse> Items, string? Next);
