namespace LabResults.Application;

/// <summary>What may appear in a log line about a patient.</summary>
public static class Masking
{
    private const int VisibleDigits = 4;

    /// <summary>"1012345678" becomes "******5678". Any other input is masked too, never logged as it came in.</summary>
    /// <param name="value">The national id as received.</param>
    /// <returns>The masked value.</returns>
    public static string NationalId(string? value) => value switch
    {
        null or "" => "",
        { Length: <= VisibleDigits } => new string('*', value.Length),
        _ => string.Create(value.Length, value, static (span, v) =>
        {
            span[..^VisibleDigits].Fill('*');
            v.AsSpan(v.Length - VisibleDigits).CopyTo(span[^VisibleDigits..]);
        }),
    };
}
