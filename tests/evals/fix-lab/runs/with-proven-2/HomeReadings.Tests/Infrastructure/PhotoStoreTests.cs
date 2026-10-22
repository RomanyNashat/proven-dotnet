using System.Text;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HomeReadings.Tests.Infrastructure;

public sealed class PhotoStoreTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "photo-store-tests", Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public PhotoStoreTests() => _root = Path.Combine(_base, "photos");

    public void Dispose()
    {
        if (Directory.Exists(_base))
        {
            Directory.Delete(_base, recursive: true);
        }
    }

    private PhotoStore Store() => new(Options.Create(new StorageOptions { PhotoRoot = _root }));

    private static FormFile Photo(string fileName, string contentType = "image/jpeg")
    {
        var bytes = Encoding.ASCII.GetBytes("not really a jpeg");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    [Fact]
    public async Task SaveAsync_FileNameClimbsOutOfTheRoot_StaysInsideTheRoot()
    {
        var path = await Store().SaveAsync(7, Photo("../../escaped.jpg"), CancellationToken.None);

        Path.GetFullPath(path).Should().StartWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar);
        File.Exists(Path.Combine(_base, "escaped.jpg")).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_FileNameIsAbsolute_StaysInsideTheRoot()
    {
        var outside = Path.Combine(_base, "absolute.jpg");

        var path = await Store().SaveAsync(7, Photo(outside), CancellationToken.None);

        Path.GetFullPath(path).Should().StartWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar);
        File.Exists(outside).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_TwoUploadsWithTheSameName_KeepsBothAndNeverUsesTheClientName()
    {
        var store = Store();

        var first = await store.SaveAsync(7, Photo("meter.jpg"), CancellationToken.None);
        var second = await store.SaveAsync(7, Photo("meter.jpg"), CancellationToken.None);

        first.Should().NotBe(second);
        Path.GetFileName(first).Should().NotBe("meter.jpg");
        File.Exists(first).Should().BeTrue();
        File.Exists(second).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_NotAnAcceptedType_Throws()
    {
        var save = () => Store().SaveAsync(7, Photo("x.sh", "application/x-sh"), CancellationToken.None);

        await save.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Delete_SavedPhoto_RemovesIt()
    {
        var store = Store();
        var path = await store.SaveAsync(7, Photo("meter.jpg"), CancellationToken.None);

        store.Delete(path);

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Delete_PathOutsideTheRoot_LeavesItAlone()
    {
        Directory.CreateDirectory(_base);
        var outside = Path.Combine(_base, "keep.txt");
        File.WriteAllText(outside, "keep");

        Store().Delete(Path.Combine(_root, "..", "keep.txt"));

        File.Exists(outside).Should().BeTrue();
    }

    [Theory]
    [InlineData("image/jpeg", 10, true)]
    [InlineData("image/png", 10, true)]
    [InlineData("text/plain", 10, false)]
    [InlineData("image/jpeg", 0, false)]
    [InlineData("image/jpeg", PhotoStore.MaxBytes + 1, false)]
    public void IsAcceptable_ChecksTypeAndSize(string contentType, long length, bool expected)
    {
        var file = new FormFile(Stream.Null, 0, length, "photo", "x") { Headers = new HeaderDictionary(), ContentType = contentType };

        PhotoStore.IsAcceptable(file).Should().Be(expected);
    }
}
