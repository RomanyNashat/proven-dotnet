using Microsoft.Extensions.Compliance.Redaction;

namespace SkillSamples.PiiMasking;

/// <summary>Replaces any value with four stars, so a reader sees a value was there and not how long it was.</summary>
public sealed class StarRedactor : Redactor
{
    private const string Stars = "****";

    public override int GetRedactedLength(ReadOnlySpan<char> input) => Stars.Length;

    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        Stars.AsSpan().CopyTo(destination);
        return Stars.Length;
    }
}
