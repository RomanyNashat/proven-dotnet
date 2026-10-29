using System.Diagnostics.CodeAnalysis;

namespace LabResults.Application;

/// <summary>Saudi national ID or iqama: exactly 10 digits.</summary>
public static class NationalId
{
    private const int Length = 10;

    /// <summary>Normalises the digits to ASCII and accepts exactly 10 of them, nothing else.</summary>
    /// <param name="input">The ID as typed.</param>
    /// <param name="normalized">The ID in ASCII digits, when valid.</param>
    /// <returns>Whether <paramref name="input"/> is a 10-digit ID.</returns>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (input is null)
        {
            return false;
        }

        var ascii = Digits.ToAscii(input.Trim());
        if (ascii.Length != Length || !Digits.AreAllAscii(ascii))
        {
            return false;
        }

        normalized = ascii;
        return true;
    }
}
