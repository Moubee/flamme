using Elsa.Http.DownloadableContentHandlers;
using Elsa.Http.Services;
using TUnit.Assertions.Enums;

namespace Elsa.Http.UnitTests.DownloadableContentHandlers;

public class BinaryDownloadableContentHandlerTests
{
    [Test]
    public async Task GetDownloadable_ExposesReadOnlySeekableBytesAndMetadata()
    {
        byte[] bytes = [0, 1, 127, 128, 255, 0];
        var downloadable = await GetDownloadableAsync(bytes);
        await using var stream = downloadable.Stream;

        await Assert.That(downloadable.Filename).IsEqualTo("file.bin");
        await Assert.That(downloadable.ContentType).IsEqualTo("application/octet-stream");
        await Assert.That(stream.CanRead).IsTrue();
        await Assert.That(stream.CanSeek).IsTrue();
        await Assert.That(stream.CanWrite).IsFalse();
        await Assert.That(stream.Length).IsEqualTo(bytes.Length);

        var actual = new byte[bytes.Length];
        await stream.ReadExactlyAsync(actual);
        await Assert.That(actual).IsEquivalentTo(bytes, CollectionOrdering.Matching);

        stream.Seek(-3, SeekOrigin.End);
        var range = new byte[3];
        await stream.ReadExactlyAsync(range);
        await Assert.That(range).IsEquivalentTo(bytes[^3..], CollectionOrdering.Matching);
    }

    [Test]
    public async Task GetDownloadable_EmptyContent_HasZeroLengthAndNoData()
    {
        var downloadable = await GetDownloadableAsync([]);
        await using var stream = downloadable.Stream;

        await Assert.That(stream.Length).IsEqualTo(0L);
        await Assert.That(stream.ReadByte()).IsEqualTo(-1);
    }

    [Test]
    public async Task GetDownloadable_DisposedStream_RejectsReads()
    {
        var downloadable = await GetDownloadableAsync([1, 2, 3]);
        var stream = downloadable.Stream;
        await stream.DisposeAsync();

        await Assert.That(stream.CanRead).IsFalse();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Task.FromResult(stream.ReadByte()));
    }

    private static ValueTask<Downloadable> GetDownloadableAsync(byte[] bytes)
    {
        var manager = new DefaultDownloadableManager([new BinaryDownloadableContentHandler()]);
        return manager.GetDownloadablesAsync(bytes).Single()();
    }
}
