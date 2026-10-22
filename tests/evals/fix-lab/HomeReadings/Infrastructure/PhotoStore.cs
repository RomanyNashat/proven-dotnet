namespace HomeReadings.Infrastructure;

/// <summary>Photos of the meter's screen, kept on the photo volume; the reading keeps the path.</summary>
public sealed class PhotoStore(IConfiguration configuration)
{
    private readonly string _root = configuration["Storage:PhotoRoot"]!;

    public async Task<string> SaveAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        var folder = Path.Combine(_root, readingId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, file.FileName);
        await using var stream = File.Create(path);
        await file.CopyToAsync(stream, ct);
        return path;
    }
}
