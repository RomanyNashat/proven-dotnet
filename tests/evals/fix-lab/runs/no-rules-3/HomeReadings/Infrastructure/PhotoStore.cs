using System.Globalization;
using HomeReadings.Application;

namespace HomeReadings.Infrastructure;

/// <summary>Photos of the meter's screen, kept on the photo volume; the reading keeps the path relative to the root.</summary>
public sealed class PhotoStore(IConfiguration configuration, ILogger<PhotoStore> logger)
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
    };

    private readonly string _root = Path.GetFullPath(configuration["Storage:PhotoRoot"]!);

    public async Task<string> SaveAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > MaxBytes)
        {
            throw new RequestRejectedException("Photo must be between 1 byte and 10 MB");
        }

        if (!Extensions.TryGetValue(file.ContentType ?? "", out var extension))
        {
            throw new RequestRejectedException("Photo must be a JPEG or PNG image");
        }

        // The client's file name is never used: it could point outside the photo root or overwrite another photo.
        var relative = Path.Combine(readingId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N") + extension);
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(stream, ct);
        }
        catch
        {
            Delete(relative);
            throw;
        }

        return relative;
    }

    public void Delete(string relative)
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(_root, relative));
            if (path.StartsWith(_root, StringComparison.Ordinal))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete photo {Path}", relative);
        }
    }
}
