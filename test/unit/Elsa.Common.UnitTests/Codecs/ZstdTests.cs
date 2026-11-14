using Elsa.Common.Codecs;
#if NET11_0_OR_GREATER
using System.Text;
using IronCompress;
#endif

namespace Elsa.Common.UnitTests.Codecs;

public class ZstdTests
{
    private readonly Zstd _codec = new();

    [Test]
    public async Task CompressAsync_WithSimpleString_ReturnsCompressedString()
    {
        // Arrange
        var input = "Hello, World!";

        // Act
        var result = await _codec.CompressAsync(input);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsNotEmpty();
        await Assert.That(result).IsNotEqualTo(input);
    }

    [Test]
    [Arguments("Hello, World!")]
    [Arguments("")]
    [Arguments("Hello! 你好! مرحبا! Здравствуйте! 🎉🎊")]
    [Arguments("{\"name\":\"John Doe\",\"age\":30,\"city\":\"New York\",\"items\":[1,2,3,4,5]}")]
    public async Task CompressDecompress_RoundTrip_PreservesOriginalData(string original)
    {
        // Act
        var compressed = await _codec.CompressAsync(original);
        var decompressed = await _codec.DecompressAsync(compressed);

        // Assert
        await Assert.That(decompressed).IsEquivalentTo(original, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task CompressDecompress_WithLargeString_WorksCorrectly()
    {
        // Arrange
        var original = string.Join("", Enumerable.Repeat("This is a test string that will be compressed. ", 1000));

        // Act
        var compressed = await _codec.CompressAsync(original);
        var decompressed = await _codec.DecompressAsync(compressed);

        // Assert
        await Assert.That(decompressed).IsEquivalentTo(original, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(compressed.Length < original.Length).IsTrue().Because("Compressed string should be smaller than original");
    }

    [Test]
    public async Task CompressAsync_MultipleCallsWithSameInput_ProducesConsistentResults()
    {
        // Arrange
        var input = "Test string for consistency";

        // Act
        var result1 = await _codec.CompressAsync(input);
        var result2 = await _codec.CompressAsync(input);

        // Assert
        await Assert.That(result2).IsEquivalentTo(result1, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("KLUv/SAAAQAA", "")]
    [Arguments("KLUv/SANaQAASGVsbG8sIFdvcmxkIQ==", "Hello, World!")]
    [Arguments("KLUv/SA96QEASGVsbG8hIOS9oOWlvSEg2YXYsdit2KjYpyEg0JfQtNGA0LDQstGB0YLQstGD0LnRgtC1ISDwn46J8J+Oig==", "Hello! 你好! مرحبا! Здравствуйте! 🎉🎊")]
    public async Task DecompressAsync_ReadsPersistedLegacyFrames(string compressed, string expected)
    {
        // These Base64 frames were produced by IronCompress 1.7.0.
        var actual = await _codec.DecompressAsync(compressed);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task DecompressAsync_WithEmptyBuffer_Throws()
    {
        await Assert.ThrowsAsync<Exception>(() => _codec.DecompressAsync(string.Empty).AsTask());
    }

#if NET11_0_OR_GREATER
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DecompressAsync_RejectsTruncatedFramesAndTrailingGarbage(bool appendGarbage)
    {
        var compressed = Convert.FromBase64String(await _codec.CompressAsync("Hello, World!"));
        var damaged = appendGarbage ? compressed.Concat(new byte[] { 1 }).ToArray() : compressed[..^1];

        await Assert.ThrowsAsync<InvalidDataException>(() => _codec.DecompressAsync(Convert.ToBase64String(damaged)).AsTask());
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(10000)]
    public async Task CompressAsync_ProducesFramesReadableByLegacyCodec(int repetitions)
    {
        var original = string.Concat(Enumerable.Repeat("Hello! 你好! مرحبا! Здравствуйте! 🎉🎊", repetitions));
        var compressed = await _codec.CompressAsync(original);

        var legacyCodec = new Iron();
        using var result = legacyCodec.Decompress(Codec.Zstd, Convert.FromBase64String(compressed));
        var actual = Encoding.UTF8.GetString(result.AsSpan());

        await Assert.That(actual).IsEqualTo(original);
    }
#endif
}
