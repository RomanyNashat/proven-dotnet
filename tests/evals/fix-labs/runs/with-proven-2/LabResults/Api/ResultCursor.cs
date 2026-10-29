using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace LabResults.Api;

/// <summary>
/// Where the next page starts: the last row's time and id. The id breaks ties, so rows saved in the same
/// instant aren't skipped at a page edge. Sent as base64url, so it survives a query string unencoded.
/// </summary>
public sealed record ResultCursor(DateTimeOffset ReceivedAt, int Id)
{
    /// <summary>The opaque token returned as <c>next</c>.</summary>
    /// <returns>A URL-safe string.</returns>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{ReceivedAt.ToUniversalTime():O}|{Id}")));

    /// <summary>
    /// Reads a token made by <see cref="Encode"/>, or a plain ISO timestamp (the old <c>next</c> format),
    /// which means "strictly before this instant".
    /// </summary>
    /// <param name="token">The <c>before</c> query value.</param>
    /// <param name="cursor">The cursor, in UTC.</param>
    /// <returns><c>false</c> for anything else, which the endpoint answers with 400.</returns>
    public static bool TryParse(string? token, [NotNullWhen(true)] out ResultCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (Base64Url.IsValid(token) && TryDecode(token, out cursor))
        {
            return true;
        }

        if (token.Contains('T', StringComparison.Ordinal)
            && DateTimeOffset.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
        {
            cursor = new ResultCursor(at.ToUniversalTime(), 0);
            return true;
        }

        return false;
    }

    private static bool TryDecode(string token, [NotNullWhen(true)] out ResultCursor? cursor)
    {
        cursor = null;
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(Base64Url.DecodeFromChars(token));
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return false;
        }

        var bar = text.LastIndexOf('|');
        if (bar <= 0
            || !DateTimeOffset.TryParseExact(text[..bar], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            || !int.TryParse(text[(bar + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        cursor = new ResultCursor(at.ToUniversalTime(), id);
        return true;
    }
}
