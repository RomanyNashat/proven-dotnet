namespace SkillSamples.Uploads;

public enum UploadKind
{
    Jpeg,
    Png,
    Pdf,
}

/// <summary>
/// What a file is, from its first bytes. The name and the Content-Type come from the client and say
/// whatever the client wants; the bytes are what a browser, an image library or a PDF reader will act on.
/// </summary>
public static class FileSignature
{
    /// <summary>The bytes to read before deciding: the longest signature below.</summary>
    public const int HeadLength = 8;

    public static UploadKind? Detect(ReadOnlySpan<byte> head) => head switch
    {
        [0xFF, 0xD8, 0xFF, ..] => UploadKind.Jpeg,
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => UploadKind.Png,
        [0x25, 0x50, 0x44, 0x46, 0x2D, ..] => UploadKind.Pdf,   // "%PDF-"
        _ => null,
    };

    /// <summary>The extension and media type the server uses: chosen from the kind, never from the client.</summary>
    public static (string Extension, string MediaType) Describe(UploadKind kind) => kind switch
    {
        UploadKind.Jpeg => (".jpg", "image/jpeg"),
        UploadKind.Png => (".png", "image/png"),
        UploadKind.Pdf => (".pdf", "application/pdf"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
