using System.Globalization;
using Microsoft.Extensions.Options;

namespace HomeReadings.Infrastructure;

/// <summary>
/// Photos of the meter's screen, kept on the photo volume; the reading keeps the key. The client's file
/// name is never used: the server names the file, from the reading id and the image type it detects.
/// </summary>
public sealed class PhotoStore(IOptions<StorageOptions> options)
{
    /// <summary>The largest photo accepted. The endpoint's request limit and the ingress body limit must allow it.</summary>
    public const long MaxBytes = 10 * 1024 * 1024;

    private readonly string _root = Path.GetFullPath(options.Value.PhotoRoot);

    /// <summary>Saves the photo if it is a JPEG, PNG, HEIC or WebP image within <see cref="MaxBytes"/>.</summary>
    /// <param name="readingId">The reading it belongs to.</param>
    /// <param name="file">The upload.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The key, relative to the photo root (<c>{readingId}/{name}.{ext}</c>), or null when it isn't an accepted image.</returns>
    public async Task<string?> SaveAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        if (file.Length is 0 or > MaxBytes)
        {
            return null;
        }

        await using var upload = file.OpenReadStream();
        var header = new byte[12];
        var read = await upload.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (ImageExtension(header.AsSpan(0, read)) is not { } extension)
        {
            return null;
        }

        var folder = readingId.ToString(CultureInfo.InvariantCulture);
        var key = $"{folder}/{Guid.CreateVersion7():N}{extension}";
        Directory.CreateDirectory(Path.Combine(_root, folder));
        await using var stored = new FileStream(Path.Combine(_root, key), FileMode.CreateNew, FileAccess.Write);
        await stored.WriteAsync(header.AsMemory(0, read), ct);
        await upload.CopyToAsync(stored, ct);
        return key;
    }

    // By content, not by the client's file name or content type, which the client chooses.
    private static string? ImageExtension(ReadOnlySpan<byte> header) => header switch
    {
        [0xFF, 0xD8, 0xFF, ..] => ".jpg",
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => ".png",
        [_, _, _, _, (byte)'f', (byte)'t', (byte)'y', (byte)'p', .. var brand] when IsHeif(brand) => ".heic",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P'] => ".webp",
        _ => null,
    };

    private static bool IsHeif(ReadOnlySpan<byte> brand) =>
        brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8);
}
