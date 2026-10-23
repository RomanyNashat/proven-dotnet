using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace SkillSamples.Uploads;

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
