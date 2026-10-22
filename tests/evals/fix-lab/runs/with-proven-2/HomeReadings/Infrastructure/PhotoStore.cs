using System.Globalization;
using Microsoft.Extensions.Options;

namespace HomeReadings.Infrastructure;

/// <summary>Photos of the meter's screen, kept on the photo volume; the reading keeps the path.</summary>
public sealed class PhotoStore(IOptions<StorageOptions> options)
{
    /// <summary>The largest photo accepted, in bytes.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
        ["image/webp"] = ".webp",
    };

    private readonly string _root = Path.GetFullPath(options.Value.PhotoRoot!);

    /// <summary>Whether a file is a photo this store takes: an allowed image type, not empty, not over <see cref="MaxBytes"/>.</summary>
    /// <param name="file">The uploaded file.</param>
    /// <returns><c>true</c> when it can be saved.</returns>
    public static bool IsAcceptable(IFormFile file) =>
        file.Length is > 0 and <= MaxBytes && Extensions.ContainsKey(file.ContentType ?? "");

    /// <summary>Saves the photo under a name the server chooses and returns its path.</summary>
    /// <param name="readingId">The reading the photo belongs to.</param>
    /// <param name="file">The uploaded file; check it with <see cref="IsAcceptable"/> first.</param>
    /// <param name="ct">Cancels the copy.</param>
    /// <returns>The full path of the saved file.</returns>
    /// <exception cref="ArgumentException">The file is not an accepted image type.</exception>
    public async Task<string> SaveAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        if (!Extensions.TryGetValue(file.ContentType ?? "", out var extension))
        {
            throw new ArgumentException("The file is not an accepted photo type.", nameof(file));
        }

        var folder = Path.Combine(_root, readingId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        // The client's file name is never used: "../.." climbs out of the root, and an absolute name
        // makes Path.Combine drop the root altogether.
        var path = Path.Combine(folder, Guid.CreateVersion7().ToString("N") + extension);
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await file.CopyToAsync(stream, ct);
        }
        catch
        {
            Delete(path);
            throw;
        }

        return path;
    }

    /// <summary>Deletes a photo this store saved. A path outside the photo root is left alone.</summary>
    /// <param name="path">The stored path.</param>
    public void Delete(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(full))
        {
            File.Delete(full);
        }
    }
}
