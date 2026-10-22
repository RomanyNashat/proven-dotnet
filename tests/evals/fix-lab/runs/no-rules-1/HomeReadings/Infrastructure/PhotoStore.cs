using HomeReadings.Application;

namespace HomeReadings.Infrastructure;

/// <summary>Photos of the meter's screen, kept on the photo volume; the reading keeps the path.</summary>
public sealed class PhotoStore(IConfiguration configuration)
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".heic", ".webp"];

    private readonly string _root = Path.GetFullPath(configuration["Storage:PhotoRoot"]!);

    public async Task<string> SaveAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        // The client's file name is never used for the path: it could contain "../" or collide with another upload.
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.Length is 0 or > MaxBytes || !AllowedExtensions.Contains(extension))
        {
            throw new InvalidReadingException(new Dictionary<string, string[]>
            {
                ["photo"] = ["photo must be a non-empty jpg, png, heic or webp image of at most 10 MB."],
            });
        }

        var folder = Path.Combine(_root, readingId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + extension);
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            await file.CopyToAsync(stream, ct);
        }

        return path;
    }

    /// <summary>Removes a replaced photo; only files under the photo root are touched.</summary>
    public void Delete(string? path)
    {
        if (path is null || !Path.GetFullPath(path).StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
