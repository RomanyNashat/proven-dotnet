using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HomeReadings.Tests.Infrastructure;

public sealed class PhotoStoreTests : IDisposable
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "photo-tests", Guid.NewGuid().ToString("N"));

    private PhotoStore Store => new(Options.Create(new StorageOptions { PhotoRoot = _root }));

    [Theory]
    [InlineData("../../escaped-{0}.jpg")]
    [InlineData("{1}/escaped-absolute-{0}.jpg")]
    public async Task SaveAsync_FileNameLeavesTheRoot_WritesInsideTheRoot(string fileNameFormat)
    {
        var fileName = string.Format(System.Globalization.CultureInfo.InvariantCulture, fileNameFormat,
            Guid.NewGuid().ToString("N"), AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));

        var saved = await Store.SaveAsync(7, Photo(Jpeg, fileName), CancellationToken.None);

        saved.Should().NotBeNull();
        Path.GetFullPath(saved!, _root).Should().StartWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar);
        File.Exists(Path.GetFullPath(Path.Combine(_root, "7", fileName))).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_Png_IsKeptUnderTheReadingWithAServerName()
    {
        var saved = await Store.SaveAsync(7, Photo(Png, "meter.png"), CancellationToken.None);

        saved.Should().MatchRegex(@"^7/[0-9a-f]{32}\.png$");
        File.ReadAllBytes(Path.Combine(_root, saved!)).Should().Equal(Png);
    }

    [Fact]
    public async Task SaveAsync_NotAnImage_IsRefusedAndNothingIsWritten()
    {
        var saved = await Store.SaveAsync(7, Photo("#!/bin/sh\nrm -rf /"u8.ToArray(), "meter.jpg"), CancellationToken.None);

        saved.Should().BeNull();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_LargerThanTheLimit_IsRefused()
    {
        var big = new byte[PhotoStore.MaxBytes + 1];
        Jpeg.CopyTo(big, 0);

        var saved = await Store.SaveAsync(7, Photo(big, "meter.jpg"), CancellationToken.None);

        saved.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static FormFile Photo(byte[] content, string fileName) =>
        new(new MemoryStream(content), 0, content.Length, "photo", fileName) { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
}
