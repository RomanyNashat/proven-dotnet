namespace HomeReadings.Infrastructure;

/// <summary>Photos of the meter's screen, kept on the photo volume; the reading keeps the path.</summary>
public sealed class PhotoStore(IConfiguration configuration)
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/heic"] = ".heic",
        ["image/webp"] = ".webp",
    };

    private readonly string _root = Path.GetFullPath(configuration["Storage:PhotoRoot"]
        ?? throw new InvalidOperationException("Storage:PhotoRoot is not configured"));

    public async Task<string> SaveAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        if (file.Length is 0 or > MaxBytes || !Extensions.TryGetValue(file.ContentType ?? "", out var extension))
        {
            throw new Application.InvalidReadingException("Photo must be a non-empty image up to 10 MB");
        }

        var folder = Path.Combine(_root, readingId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        // Never use the client's file name: it can hold path segments, and a re-upload must not overwrite.
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + extension);
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            await file.CopyToAsync(stream, ct);
        }

        return path;
    }

    public void Delete(string? path)
    {
        if (path is not null && Path.GetFullPath(path).StartsWith(_root, StringComparison.Ordinal))
        {
            File.Delete(path);
        }
    }
}
