namespace SkillSamples.Uploads;

public sealed class UploadTooLargeException(long limit) : IOException($"The file is larger than {limit} bytes.");

/// <summary>
/// Stores an upload under a name the server makes. The file is written to a temporary name in the same
/// folder and moved into place only once it's complete and within the limit, so a reader never sees half
/// a file and a failed upload leaves nothing behind. On one volume the move is a rename.
/// </summary>
public sealed class LocalFileStore(string root, long maxBytes, TimeProvider time)
{
    /// <summary>Copies `content` (already checked by kind) and returns the key the row keeps.</summary>
    public async Task<string> SaveAsync(Stream content, UploadKind kind, CancellationToken ct)
    {
        var (extension, _) = FileSignature.Describe(kind);
        var key = $"{time.GetUtcNow():yyyy/MM}/{Guid.CreateVersion7():N}{extension}";
        var path = SafePath.Under(root, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".part";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    // Counted while copying: a stream's Length can be missing, or a client's claim.
                    written += read;
                    if (written > maxBytes)
                    {
                        throw new UploadTooLargeException(maxBytes);
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            File.Move(temporary, path);
            return key;
        }
        finally
        {
            File.Delete(temporary);   // after a successful move there's nothing there; after a failure, the half file
        }
    }

    public FileStream Open(string key) => File.OpenRead(SafePath.Under(root, key));
}
