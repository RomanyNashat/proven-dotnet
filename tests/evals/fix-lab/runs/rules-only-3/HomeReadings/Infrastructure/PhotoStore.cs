using System.Globalization;
using Microsoft.Extensions.Options;

namespace HomeReadings.Infrastructure;

/// <summary>Photos of the meter's screen, kept on the photo volume; the reading keeps the path.</summary>
public sealed class PhotoStore(IOptions<StorageOptions> options)
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Returns the file extension for a photo the store accepts (JPEG or PNG, by content), or null.</summary>
    public static async Task<string?> DetectImageTypeAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length is 0 or > MaxBytes)
        {
            return null;
        }

        var header = new byte[PngMagic.Length];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var span = header.AsSpan(0, read);
        return span.StartsWith(JpegMagic) ? ".jpg" : span.StartsWith(PngMagic) ? ".png" : null;
    }

    /// <summary>
    /// Saves the photo under a name the service chooses and returns its path relative to the photo root.
    /// The client's file name is never used: it can hold "../" or an absolute path.
    /// </summary>
    public async Task<string> SaveAsync(int readingId, IFormFile file, string extension, CancellationToken ct)
    {
        var root = Path.GetFullPath(options.Value.PhotoRoot);
        var relative = Path.Combine(readingId.ToString(CultureInfo.InvariantCulture), "photo" + extension);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write beside the target and move it into place, so a failed upload never leaves half a photo.
        var temp = path + "." + Guid.CreateVersion7().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await file.CopyToAsync(stream, ct);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }

        return relative.Replace('\\', '/');
    }
}
