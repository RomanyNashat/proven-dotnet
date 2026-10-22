using HomeReadings.Options;
using Microsoft.Extensions.Options;

namespace HomeReadings.Infrastructure;

/// <summary>Photos of the meter's screen, kept on the photo volume; the reading keeps the path.</summary>
public sealed class PhotoStore(IOptions<StorageOptions> options)
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly string _root = Path.GetFullPath(options.Value.PhotoRoot);

    /// <summary>Returns ".jpg" or ".png" when the bytes are one of those, otherwise null.</summary>
    public static async Task<string?> DetectExtensionAsync(IFormFile file, CancellationToken ct)
    {
        var header = new byte[Png.Length];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var bytes = header.AsSpan(0, read);
        return bytes.StartsWith(Jpeg) ? ".jpg" : bytes.StartsWith(Png) ? ".png" : null;
    }

    /// <summary>
    /// Saves the photo under a name the server chooses. The client's file name is never used: it can
    /// carry "../" and write anywhere the process can.
    /// </summary>
    public async Task<string> SaveAsync(int readingId, IFormFile file, string extension, CancellationToken ct)
    {
        var folder = Path.Combine(_root, readingId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var name = $"{Guid.CreateVersion7():N}{extension}";
        var path = Path.Combine(folder, name);
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await file.CopyToAsync(stream, ct);
        }

        return path;
    }

    /// <summary>Removes a photo this store wrote earlier; ignores paths outside the photo root.</summary>
    public void Delete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var full = Path.GetFullPath(path);
        if (full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(full))
        {
            File.Delete(full);
        }
    }
}
