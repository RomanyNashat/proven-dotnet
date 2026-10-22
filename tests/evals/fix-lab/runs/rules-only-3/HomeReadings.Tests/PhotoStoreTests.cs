using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HomeReadings.Tests;

public sealed class PhotoStoreTests : IDisposable
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photos-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_FileNameWithTraversal_StaysUnderTheRoot()
    {
        var store = new PhotoStore(Options.Create(new StorageOptions { PhotoRoot = _root }));

        var path = await store.SaveAsync(7, File("../../../etc/evil.sh", Jpeg), ".jpg", CancellationToken.None);

        path.Should().Be("7/photo.jpg");
        System.IO.File.ReadAllBytes(Path.Combine(_root, "7", "photo.jpg")).Should().Equal(Jpeg);
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().ContainSingle();
    }

    [Fact]
    public async Task DetectImageTypeAsync_Jpeg_IsJpg() =>
        (await PhotoStore.DetectImageTypeAsync(File("a.png", Jpeg), CancellationToken.None)).Should().Be(".jpg");

    [Fact]
    public async Task DetectImageTypeAsync_Script_IsRejected() =>
        (await PhotoStore.DetectImageTypeAsync(File("a.jpg", "#!/bin/sh"u8.ToArray()), CancellationToken.None)).Should().BeNull();

    [Fact]
    public async Task DetectImageTypeAsync_TooLarge_IsRejected()
    {
        var big = new byte[PhotoStore.MaxBytes + 1];
        Jpeg.CopyTo(big, 0);

        (await PhotoStore.DetectImageTypeAsync(File("a.jpg", big), CancellationToken.None)).Should().BeNull();
    }

    private static FormFile File(string name, byte[] content) =>
        new(new MemoryStream(content), 0, content.Length, "photo", name);
}
