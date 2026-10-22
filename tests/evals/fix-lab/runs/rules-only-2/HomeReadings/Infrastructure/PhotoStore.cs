using Microsoft.Extensions.Options;

namespace HomeReadings.Infrastructure;

/// <summary>
/// Photos of the meter's screen, kept on the photo volume; the reading keeps the path relative to the root.
/// The file name is ours, never the client's, and only JPEG, PNG and HEIC images are kept.
/// </summary>
public sealed class PhotoStore(IOptions<StorageOptions> options, ILogger<PhotoStore> logger)
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Value.PhotoRoot));

    /// <summary>Saves the photo and returns its path relative to the photo root, or null when it isn't an accepted image.</summary>
    public async Task<string?> SaveAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        if (file.Length is <= 0 or > MaxBytes)
        {
            return null;
        }

        await using var input = file.OpenReadStream();
        var header = new byte[12];
        var read = await input.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (DetectExtension(header.AsSpan(0, read)) is not { } extension)
        {
            return null;
        }

        var relative = Path.Combine(readingId.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{Guid.CreateVersion7():N}{extension}");
        var path = FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await output.WriteAsync(header.AsMemory(0, read), ct);
            await input.CopyToAsync(output, ct);
        }
        catch
        {
            File.Delete(path);
            throw;
        }

        return relative;
    }

    /// <summary>Removes a photo that is no longer referenced. A failure is logged, not thrown: the reading is already saved.</summary>
    public void Delete(string relativePath)
    {
        try
        {
            File.Delete(FullPath(relativePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not delete an old meter photo");
        }
    }

    public static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(Jpeg))
        {
            return ".jpg";
        }

        if (header.StartsWith(Png))
        {
            return ".png";
        }

        // ISO base media file: "ftyp" at offset 4, then the brand. Phone cameras save HEIC.
        if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = header.Slice(8, 4);
            if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("mif1"u8))
            {
                return ".heic";
            }
        }

        return null;
    }

    private string FullPath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A photo path left the photo root.");
        }

        return full;
    }
}
