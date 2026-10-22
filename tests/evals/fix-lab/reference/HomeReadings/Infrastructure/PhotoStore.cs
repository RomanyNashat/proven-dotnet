namespace HomeReadings.Infrastructure;

/// <summary>Photos of the meter's screen, kept on the photo volume; the reading keeps the path.</summary>
public sealed class PhotoStore(IConfiguration configuration)
{
    private readonly string _root = configuration["Storage:PhotoRoot"]!;

    public async Task<string> SaveAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        // The client's file name is never a path: the store names the file.
        var extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png"))
        {
            extension = ".jpg";
        }

        var root = Path.GetFullPath(_root);
        var folder = Path.Combine(root, readingId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.CreateVersion7().ToString("N") + extension);
        if (!Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("photo path outside the photo root");
        }

        await using var stream = File.Create(path);
        await file.CopyToAsync(stream, ct);
        return path;
    }
}
