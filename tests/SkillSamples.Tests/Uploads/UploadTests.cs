using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Uploads;

public sealed class UploadTests : IDisposable
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] Html = "<html><script>alert(document.cookie)</script></html>"u8.ToArray();

    private readonly string _base = Path.Combine(Path.GetTempPath(), "uploads-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public UploadTests()
    {
        _root = Path.Combine(_base, "volume", "photos");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private LocalFileStore Store(long max = UploadEndpoints.MaxPhotoBytes) =>
        new(_root, max, new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero)));

    private async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.MapPhotoUploads(Store());
        await app.StartAsync();
        return app;
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "photo", fileName);
        return form;
    }

    private string[] FilesOutsideRoot() =>
        Directory.EnumerateFiles(_base, "*", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToArray();

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }, UploadKind.Jpeg)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, UploadKind.Png)]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, UploadKind.Pdf)]
    public void Detect_KnownSignature_ReturnsItsKind(byte[] head, UploadKind expected) =>
        Assert.Equal(expected, FileSignature.Detect(head));

    [Theory]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C })]   // "<html"
    [InlineData(new byte[] { 0xFF, 0xD8 })]                     // a JPEG cut short
    [InlineData(new byte[0])]
    public void Detect_AnythingElse_ReturnsNull(byte[] head) => Assert.Null(FileSignature.Detect(head));

    [Theory]
    [InlineData("../../escape.jpg")]
    [InlineData("/etc/passwd")]                 // rooted: Path.Combine would drop the root entirely
    [InlineData("../photos-old/x.jpg")]         // a sibling whose name starts with the root's
    public void Under_PathLeavingTheRoot_Throws(string relative) =>
        Assert.Throws<PathOutsideRootException>(() => SafePath.Under(_root, relative));

    [Fact]
    public void Under_KeyInsideTheRoot_ReturnsItsFullPath() =>
        Assert.Equal(Path.Combine(_root, "2026", "10", "a.jpg"), SafePath.Under(_root, "2026/10/a.jpg"));

    [Fact]
    public async Task SaveAsync_OverTheLimit_LeavesNothingBehind()
    {
        var store = Store(max: 10);
        await Assert.ThrowsAsync<UploadTooLargeException>(() =>
            store.SaveAsync(new MemoryStream(new byte[11]), UploadKind.Jpeg, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(_base, "*", SearchOption.AllDirectories));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_APhotoNamedToClimbOut_IsStoredInsideUnderTheServersName()
    {
        // Given the app uploads a real JPEG whose file name tries to climb out of the photo folder
        await using var app = await StartAsync();
        using var response = await app.GetTestClient().PostAsync("/photos", Form(Jpeg, "../../../evil.jpg", "image/jpeg"));

        // Then it's stored, under a name the server made, and nothing was written outside the root
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var key = (await response.Content.ReadAsStringAsync()).Trim('"');
        Assert.Matches(@"^2026/10/[0-9a-f]{32}\.jpg$", key);
        Assert.Empty(FilesOutsideRoot());
        Assert.DoesNotContain("evil", string.Join(",", Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)), StringComparison.Ordinal);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_HtmlSentAsAJpeg_IsRefusedAndNeverServed()
    {
        // Given a client sends HTML with a .jpg name and Content-Type image/jpeg
        await using var app = await StartAsync();
        using var response = await app.GetTestClient().PostAsync("/photos", Form(Html, "cat.jpg", "image/jpeg"));

        // Then the bytes decide: it's refused, and nothing is stored
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Directory.EnumerateFiles(_base, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Get_StoredPhoto_IsServedWithTheServersTypeAndNosniff()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        using var created = await client.PostAsync("/photos", Form(Png, "photo.jpg", "image/jpeg"));   // a PNG named .jpg
        var key = (await created.Content.ReadAsStringAsync()).Trim('"');

        using var served = await client.GetAsync($"/photos/{key}");
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", string.Join(",", served.Headers.GetValues("X-Content-Type-Options")));
        Assert.EndsWith(".png", key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_KeyThatClimbsOut_IsNotFound()
    {
        await using var app = await StartAsync();
        File.WriteAllText(Path.Combine(_base, "secret.jpg"), "not yours");
        using var response = await app.GetTestClient().GetAsync("/photos/..%2F..%2Fsecret.jpg");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_FormLargerThanItsLimit_IsRefused()
    {
        await using var app = await StartAsync();
        var big = new byte[UploadEndpoints.MaxPhotoBytes + 128 * 1024];
        Jpeg.CopyTo(big, 0);
        using var response = await app.GetTestClient().PostAsync("/photos", Form(big, "big.jpg", "image/jpeg"));
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, $"{(int)response.StatusCode}");
        Assert.Empty(Directory.EnumerateFiles(_base, "*", SearchOption.AllDirectories));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_OnASlimImage_TheSameChecksHold()
    {
        ProductionConditions.Require();
        await using var app = await StartAsync();
        using var climbing = await app.GetTestClient().PostAsync("/photos", Form(Jpeg, "..\\..\\..\\evil.jpg", "image/jpeg"));
        Assert.Equal(HttpStatusCode.Created, climbing.StatusCode);
        Assert.Empty(FilesOutsideRoot());
        Assert.Throws<PathOutsideRootException>(() => SafePath.Under(_root, "../../x"));
    }
}
