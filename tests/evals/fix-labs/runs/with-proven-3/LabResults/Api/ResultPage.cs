namespace LabResults.Api;

/// <summary>One page, newest first. <see cref="Next"/> is opaque: pass it back as <c>before</c>.</summary>
public sealed record ResultPage(IReadOnlyList<ResultResponse> Items, string? Next);
