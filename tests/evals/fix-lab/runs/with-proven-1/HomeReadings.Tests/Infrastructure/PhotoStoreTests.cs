using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HomeReadings.Tests.Infrastructure;

public sealed class PhotoStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photo-store-tests", Guid.NewGuid().ToString("N"));
    private readonly PhotoStore _store;

    public PhotoStoreTests()
    {
        _store = new PhotoStore(Options.Create(new StorageOptions { PhotoRoot = _root }));
    }

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(_root)))
        {
            Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);
        }
    }

    private static FormFile Photo(string fileName, string contentType = "image/jpeg", int length = 16) =>
        new(new MemoryStream(new byte[length]), 0, length, "photo", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };

    [Theory]
    [InlineData("../../escape.jpg")]
    [InlineData("/tmp/escape.jpg")]
    [InlineData("..\\..\\escape.jpg")]
    public async Task SaveAsync_FileNameClimbsOut_StoresUnderTheReadingsFolder(string fileName)
    {
        var key = await _store.SaveAsync(42, Photo(fileName), CancellationToken.None);

        key.Should().StartWith("42/").And.EndWith(".jpg").And.NotContain("..").And.NotContain("escape");
        var full = Path.GetFullPath(Path.Combine(_root, key));
        full.Should().StartWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar);
        File.Exists(full).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_TwoPhotos_DoNotOverwriteEachOther()
    {
        var first = await _store.SaveAsync(42, Photo("meter.jpg"), CancellationToken.None);
        var second = await _store.SaveAsync(42, Photo("meter.jpg"), CancellationToken.None);

        first.Should().NotBe(second);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    [InlineData("")]
    public void Check_TypeNotAllowed_ReturnsError(string contentType)
    {
        PhotoStore.Check(Photo("meter.jpg", contentType)).Should().NotBeNull();
    }

    [Fact]
    public void Check_TooLarge_ReturnsError()
    {
        PhotoStore.Check(Photo("meter.jpg", length: PhotoStore.MaxBytes + 1)).Should().NotBeNull();
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("IMAGE/HEIC")]
    public void Check_AllowedPhoto_ReturnsNull(string contentType)
    {
        PhotoStore.Check(Photo("meter", contentType)).Should().BeNull();
    }
}
