namespace MeterSync.Infrastructure;

/// <summary>Saudi mobile numbers in the form the SMS gateway takes: +9665XXXXXXXX.</summary>
public static class SaudiMobile
{
    /// <summary>
    /// Reads a stored phone number written any of the usual local ways (05XXXXXXXX, 5XXXXXXXX,
    /// 9665XXXXXXXX, 009665XXXXXXXX, +966 5X XXX XXXX) into +9665XXXXXXXX.
    /// </summary>
    /// <param name="phone">The stored number.</param>
    /// <returns>The number for the gateway, or null when it isn't a Saudi mobile number.</returns>
    public static string? ToGatewayNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(c => c is not (' ' or '-' or '(' or ')')).ToArray());
        if (digits.StartsWith('+'))
        {
            digits = digits[1..];
        }
        else if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        if (!digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        var national = digits switch
        {
            { Length: 12 } when digits.StartsWith("9665", StringComparison.Ordinal) => digits[3..],
            { Length: 10 } when digits.StartsWith("05", StringComparison.Ordinal) => digits[1..],
            { Length: 9 } when digits.StartsWith('5') => digits,
            _ => null,
        };

        return national is null ? null : "+966" + national;
    }
}
