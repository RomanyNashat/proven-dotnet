using FluentAssertions;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HomeReadings.Tests;

public sealed class PhotoStoreTests : IDisposable
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x02, 0x03];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "photostore-" + Guid.NewGuid().ToString("N"));
    private readonly PhotoStore _store;

    public PhotoStoreTests() =>
        _store = new PhotoStore(Options.Create(new StorageOptions { PhotoRoot = _root }), NullLogger<PhotoStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_FileNameWithTraversal_SavesUnderReadingFolderWithOurName()
    {
        var saved = await _store.SaveAsync(7, FormFile(JpegBytes, "../../../etc/cron.d/evil"), CancellationToken.None);

        saved.Should().NotBeNull();
        saved.Should().StartWith("7" + Path.DirectorySeparatorChar).And.EndWith(".jpg").And.NotContain("..");
        File.ReadAllBytes(Path.Combine(_root, saved!)).Should().Equal(JpegBytes);
    }

    [Fact]
    public async Task SaveAsync_NotAnImage_ReturnsNullAndWritesNothing()
    {
        var saved = await _store.SaveAsync(7, FormFile("<script>alert(1)</script>"u8.ToArray(), "photo.jpg"), CancellationToken.None);

        saved.Should().BeNull();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_Empty_ReturnsNull() =>
        (await _store.SaveAsync(7, FormFile([], "photo.jpg"), CancellationToken.None)).Should().BeNull();

    [Fact]
    public async Task Delete_SavedPhoto_RemovesFile()
    {
        var saved = await _store.SaveAsync(7, FormFile(JpegBytes, "photo.jpg"), CancellationToken.None);

        _store.Delete(saved!);

        File.Exists(Path.Combine(_root, saved!)).Should().BeFalse();
    }

    [Fact]
    public void Delete_PathOutsideRoot_DoesNotDelete()
    {
        var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(outside, "keep");
        try
        {
            _store.Delete(Path.Combine("..", Path.GetFileName(outside)));

            File.Exists(outside).Should().BeTrue();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    private static FormFile FormFile(byte[] bytes, string fileName) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "photo", fileName);
}
