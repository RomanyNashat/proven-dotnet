using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace HomeReadings.Infrastructure;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    [Required]
    public string? PhotoRoot { get; set; }
}

/// <summary>
/// Photos of the meter's screen, kept on the photo volume. The reading keeps the photo's key, relative to the
/// root, so moving the files to object storage later is a copy, not a data migration.
/// </summary>
public sealed class PhotoStore(IOptions<StorageOptions> options)
{
    public const int MaxBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/heic"] = ".heic",
    };

    private readonly string _root = Path.GetFullPath(options.Value.PhotoRoot!);

    /// <summary>Why the upload can't be stored, or null when it can.</summary>
    public static string? Check(IFormFile file) =>
        file.Length is <= 0 or > MaxBytes ? $"photo must be between 1 byte and {MaxBytes / (1024 * 1024)} MB."
        : !Extensions.ContainsKey(file.ContentType ?? "") ? "photo must be a JPEG, PNG or HEIC image."
        : null;

    /// <summary>
    /// Stores a photo passed by <see cref="Check"/>. The name is made here: the client's file name never
    /// reaches the path.
    /// </summary>
    /// <returns>The photo's key, relative to the root.</returns>
    public async Task<string> SaveAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        if (Check(file) is not null || !Extensions.TryGetValue(file.ContentType, out var extension))
        {
            throw new ArgumentException("The photo must be checked before it is saved.", nameof(file));
        }

        var key = $"{readingId.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{Guid.CreateVersion7():N}{extension}";
        var path = FullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await file.CopyToAsync(stream, ct);
        return key;
    }

    public void Delete(string key) => File.Delete(FullPath(key));

    private string FullPath(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new InvalidOperationException("A photo key resolved outside the photo root.");
    }
}
