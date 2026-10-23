---
name: file-uploads
description: Accepting uploaded files (a photo, an image, a PDF) in ASP.NET Core safely — check what the file really is from its first bytes, never the name or Content-Type the client sent; save it under a name the server makes, so path traversal can't leave the folder (Path.Combine drops the root for a rooted name); count the size while copying; store it whole or not at all; serve it back with the server's type and nosniff. Tested in CI, also under slim-image conditions.
version: 1.0.0
---

# File uploads

An upload is the request where the client chooses everything: the bytes, the name, the declared type
and the size. Each one turned up in this harness's own review evals: a photo saved under the client's file name
(`../../`, or `/app/HomeReadings.dll`, written anywhere the process can), a type taken from
`Content-Type`, no size limit. The code below is tested in CI; the tests are in
`tests/SkillSamples.Tests/Uploads/`.

## 1. What the file is: its first bytes

The name and the `Content-Type` say whatever the client wants. The first bytes are what a browser, an
image library or a PDF reader acts on, so they decide. Allow a short list, and refuse the rest.

<!-- sample: tests/SkillSamples.Tests/Uploads/FileSignature.cs -->
```csharp
public enum UploadKind
{
    Jpeg,
    Png,
    Pdf,
}

/// <summary>
/// What a file is, from its first bytes. The name and the Content-Type come from the client and say
/// whatever the client wants; the bytes are what a browser, an image library or a PDF reader will act on.
/// </summary>
public static class FileSignature
{
    /// <summary>The bytes to read before deciding: the longest signature below.</summary>
    public const int HeadLength = 8;

    public static UploadKind? Detect(ReadOnlySpan<byte> head) => head switch
    {
        [0xFF, 0xD8, 0xFF, ..] => UploadKind.Jpeg,
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => UploadKind.Png,
        [0x25, 0x50, 0x44, 0x46, 0x2D, ..] => UploadKind.Pdf,   // "%PDF-"
        _ => null,
    };

    /// <summary>The extension and media type the server uses: chosen from the kind, never from the client.</summary>
    public static (string Extension, string MediaType) Describe(UploadKind kind) => kind switch
    {
        UploadKind.Jpeg => (".jpg", "image/jpeg"),
        UploadKind.Png => (".png", "image/png"),
        UploadKind.Pdf => (".pdf", "application/pdf"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
```

A signature says what the file claims to be at the start; it doesn't prove the rest is harmless. For
files someone else will open (a PDF a doctor reads), a malware scan is the next layer, done by the
platform's scanner, not by hand in the service.

## 2. Where it goes: a name the server makes, under a root it can't leave

`Path.Combine(root, file.FileName)` has two holes: `../` climbs out, and a **rooted second argument
replaces the root**: `Path.Combine("/data/photos", "/app/x.dll")` is `/app/x.dll`. Never use the
client's name for the path. Make the name, and check every path you build ends up under the root:

<!-- sample: tests/SkillSamples.Tests/Uploads/SafePath.cs -->
```csharp
public sealed class PathOutsideRootException(string path) : IOException($"'{path}' is outside the storage root.");

/// <summary>
/// Joins a storage root and a relative key, and refuses any result outside the root. Path.Combine alone
/// doesn't: "../" climbs out, and a rooted second argument ("/app/x.dll", "C:\x") replaces the root
/// entirely.
/// </summary>
public static class SafePath
{
    private static readonly StringComparison Comparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Under(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root);
        var rootWithSeparator = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        // The separator matters: "/data/photos-old" starts with "/data/photos" too.
        if (!full.StartsWith(rootWithSeparator, Comparison))
        {
            throw new PathOutsideRootException(relative);
        }

        return full;
    }
}
```

The row keeps the **key** (`2026/10/<guid>.jpg`), never the absolute path: the root can move, and on
SQL Server and PostgreSQL alike the column stays bounded (`rules/efcore-rules.md`: no binary columns, the
file lives outside the database). A volume local to one pod isn't shared across pods: in production the
store is object storage, with the same key and the same rules.

## 3. Writing it: counted while copying, whole or not at all

<!-- sample: tests/SkillSamples.Tests/Uploads/LocalFileStore.cs -->
```csharp
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
```

## 4. The endpoint, and serving it back

<!-- sample: tests/SkillSamples.Tests/Uploads/UploadEndpoints.cs -->
```csharp
public static class UploadEndpoints
{
    public const long MaxPhotoBytes = 5 * 1024 * 1024;

    public static IEndpointRouteBuilder MapPhotoUploads(this IEndpointRouteBuilder app, LocalFileStore store)
    {
        // Three limits on the way in, kept in step: the ingress (nginx proxy-body-size, 1 MB by default),
        // Kestrel's request body (30,000,000 bytes by default, lowered here per endpoint), and the form
        // reader's multipart limit. The store counts again while it copies.
        app.MapPost("/photos", async Task<Results<Created<string>, ValidationProblem>> (IFormFile photo, CancellationToken ct) =>
            {
                await using var stream = photo.OpenReadStream();
                var head = new byte[FileSignature.HeadLength];
                var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
                if (FileSignature.Detect(head.AsSpan(0, read)) is not ({ } kind and (UploadKind.Jpeg or UploadKind.Png)))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["photo"] = ["Only JPEG or PNG images."] });
                }

                stream.Position = 0;
                try
                {
                    var key = await store.SaveAsync(stream, kind, ct);
                    return TypedResults.Created($"/photos/{key}", key);
                }
                catch (UploadTooLargeException)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["photo"] = [$"At most {MaxPhotoBytes} bytes."] });
                }
            })
            .WithMetadata(new RequestSizeLimitAttribute(MaxPhotoBytes + 64 * 1024))   // the file plus the form's own bytes
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxPhotoBytes + 64 * 1024 })
            .DisableAntiforgery();   // a bearer-token API: no cookie, so nothing for a forged form to ride on

        // Served with the type the server chose and nosniff, so a browser can't be talked into reading an
        // image as HTML. The key came from the server; the caller's right to this file is checked by owner
        // like any other id (rules/security.md).
        app.MapGet("/photos/{**key}", Results<FileStreamHttpResult, NotFound> (string key, HttpContext http) =>
        {
            var extension = Path.GetExtension(key);
            var kind = extension switch { ".jpg" => UploadKind.Jpeg, ".png" => UploadKind.Png, _ => (UploadKind?)null };
            if (kind is null)
            {
                return TypedResults.NotFound();
            }

            try
            {
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return TypedResults.File(store.Open(key), FileSignature.Describe(kind.Value).MediaType);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or PathOutsideRootException)
            {
                return TypedResults.NotFound();
            }
        });
        return app;
    }
}
```

**Three limits, in step.** A body passes the ingress first (nginx `client_max_body_size`, or the ingress
annotation `proxy-body-size`: 1 MB by default), then Kestrel (`MaxRequestBodySize`, 30,000,000 bytes by
default), then the form reader (`MultipartBodyLengthLimit`, 128 MB by default). A phone photo over 1 MB
gets a 413 from the ingress before the service sees it; a limit set only in the service does nothing
for that. Agree the number with whoever owns the ingress (`nginx`), set the endpoint's limits just
above it, and count again in the store.

**Serving:** the server's media type and `X-Content-Type-Options: nosniff`, so a browser can't be talked
into reading an image as HTML. A key from a URL is still a lookup by id: check the caller owns the file
(`rules/security.md`, ownership), as for any record.

**Antiforgery:** a minimal API that binds `IFormFile` asks for antiforgery. For a bearer-token API, with
no cookie, there's nothing for a forged form to ride on, so `.DisableAntiforgery()` is right. An app with
cookie sign-in keeps antiforgery on.

## What the tests show

- A real JPEG named `../../../evil.jpg` is stored inside the root, under a name the server made, and
  nothing is written outside it (story test).
- HTML sent as `cat.jpg` with `Content-Type: image/jpeg` is refused, and nothing is stored (story test).
- A PNG uploaded as `photo.jpg` is stored and served as `image/png`, with `nosniff`.
- `SafePath.Under` refuses `../../`, a rooted path, and a sibling folder whose name starts with the root's.
- A file over the limit leaves no half file behind; a form over its limit is refused.
- On a slim image (no ICU, no tzdata, UTC), a Windows-style `..\..\` name is stored inside the root too
  (production test).

## Review checklist (used by `security-reviewer` and `code-reviewer`)

- [ ] The type comes from the bytes, from an allowlist; the client's name and `Content-Type` are ignored.
- [ ] The path is built from a server-made name and checked to stay under the root.
- [ ] Size: limits at the ingress, Kestrel and the form, in step; counted again while copying.
- [ ] No half file on failure; the row keeps a bounded key, not the path or the bytes.
- [ ] Served with the server's type and `nosniff`; the caller's ownership checked.
