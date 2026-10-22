namespace HomeReadings.Domain;

/// <summary>A Saudi national ID (starts with 1) or iqama number (starts with 2): ten digits.</summary>
public static class NationalId
{
    public const int Length = 10;

    /// <summary>Checks the format and returns the ID in ASCII digits.</summary>
    /// <param name="text">The ID as sent.</param>
    /// <param name="nationalId">The ID in ASCII digits, when it is well formed.</param>
    /// <returns>Whether it is a well-formed national ID or iqama number.</returns>
    public static bool TryNormalize(string? text, out string nationalId)
    {
        nationalId = text is null ? "" : Digits.ToAscii(text.Trim());
        var valid = nationalId.Length == Length && nationalId[0] is '1' or '2' && nationalId.All(char.IsAsciiDigit);
        if (!valid)
        {
            nationalId = "";
        }

        return valid;
    }
}
