using HomeReadings.Infrastructure;
using HomeReadings.Options;
using Microsoft.AspNetCore.Http;

namespace HomeReadings.Tests;

public sealed class PhotoStoreTests : IDisposable
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photo-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_FileNameWithTraversal_StaysUnderReadingFolder()
    {
        var store = new PhotoStore(Microsoft.Extensions.Options.Options.Create(new StorageOptions { PhotoRoot = _root }));
        var file = File(JpegBytes, "../../../escape.jpg");

        var path = await store.SaveAsync(42, file, ".jpg", CancellationToken.None);

        Path.GetDirectoryName(path).Should().Be(Path.Combine(_root, "42"));
        Path.GetFileName(path).Should().NotContain("escape");
        System.IO.File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task DetectExtensionAsync_NotAnImage_ReturnsNull()
    {
        (await PhotoStore.DetectExtensionAsync(File("<html></html>"u8.ToArray(), "a.jpg"), CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task DetectExtensionAsync_Jpeg_ReturnsJpg()
    {
        (await PhotoStore.DetectExtensionAsync(File(JpegBytes, "a.png"), CancellationToken.None))
            .Should().Be(".jpg");
    }

    [Fact]
    public void Delete_PathOutsideRoot_LeavesFileAlone()
    {
        var outside = Path.GetTempFileName();
        var store = new PhotoStore(Microsoft.Extensions.Options.Options.Create(new StorageOptions { PhotoRoot = _root }));

        store.Delete(outside);

        System.IO.File.Exists(outside).Should().BeTrue();
        System.IO.File.Delete(outside);
    }

    private static FormFile File(byte[] bytes, string name) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "photo", name);
}
