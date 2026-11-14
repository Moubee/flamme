using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using GZipCodec = Elsa.Common.Codecs.GZip;

namespace Elsa.Common.UnitTests.Codecs;

public class GZipTests
{
    private readonly GZipCodec _codec = new();

    [Test]
    [Arguments("", 0u)]
    [Arguments("Hello, World!", 0xec4ac3d0u)]
    [Arguments("Hello! 你好! مرحبا! Здравствуйте! 🎉🎊", 0x2e369744u)]
    public async Task CompressAsync_ProducesCompleteInteroperableFrame(string original, uint expectedCrc)
    {
        var compressed = Convert.FromBase64String(await _codec.CompressAsync(original, CancellationToken.None));

        await Assert.That(compressed.Length).IsGreaterThanOrEqualTo(18);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(compressed.AsSpan(compressed.Length - 8))).IsEqualTo(expectedCrc);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(compressed.AsSpan(compressed.Length - 4))).IsEqualTo((uint)Encoding.UTF8.GetByteCount(original));

        using var input = new MemoryStream(compressed, writable: false);
        await using var decoder = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(decoder, Encoding.UTF8);

        await Assert.That(await reader.ReadToEndAsync()).IsEqualTo(original);
    }

    [Test]
    [Arguments("H4sIAAAAAAAC/wMAAAAAAAAAAAA=", "")]
    [Arguments("H4sIAAAAAAAC//NIzcnJ11EIzy/KSVEEANDDSuwNAAAA", "Hello, World!")]
    [Arguments("H4sIAAAAAAAC//NIzcnJV1R4snfB06V7FRVutt7YeGPtjRU3lisqXJh+YcvFhgsbLmy62HixCUg2X9gJpLcqKnyY39cJxF0ARJc2Lj0AAAA=", "Hello! 你好! مرحبا! Здравствуйте! 🎉🎊")]
    public async Task DecompressAsync_ReadsFramesProducedByPythonGzip(string compressed, string expected)
    {
        var actual = await _codec.DecompressAsync(compressed, CancellationToken.None);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task RoundTrip_LargeUnicodeInput_PreservesTextAcrossStreamingBoundaries()
    {
        var original = string.Concat(Enumerable.Repeat("Hello! 你好! مرحبا! Здравствуйте! 🎉🎊", 10000));

        var compressed = await _codec.CompressAsync(original, CancellationToken.None);
        var actual = await _codec.DecompressAsync(compressed, CancellationToken.None);

        await Assert.That(actual).IsEqualTo(original);
    }

    [Test]
    [Arguments(true, "")]
    [Arguments(true, "Hello, World!")]
    [Arguments(false, "H4sIAAAAAAAC//NIzcnJ11EIzy/KSVEEANDDSuwNAAAA")]
    public async Task CodecOperations_ObserveCancellation(bool compress, string input)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => compress
            ? _codec.CompressAsync(input, cancellation.Token).AsTask()
            : _codec.DecompressAsync(input, cancellation.Token).AsTask());
    }
}
