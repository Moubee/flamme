#if NET11_0_OR_GREATER
using System.IO.Compression;
#endif
using System.Text;
#if !NET11_0_OR_GREATER
using IronCompress;
#endif

namespace Elsa.Common.Codecs;

/// <summary>
/// Represents a ZSTD compression strategy.
/// </summary>
public class Zstd : ICompressionCodec
{
#if !NET11_0_OR_GREATER
    private Iron Iron { get; set; } = new();
#endif

    /// <inheritdoc />
    public ValueTask<string> CompressAsync(string input, CancellationToken cancellationToken = default)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
#if NET11_0_OR_GREATER
        using var output = new MemoryStream();
        using (var compressionStream = new ZstandardStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            // Legacy readers need the original size in the frame header.
            compressionStream.SetSourceLength(inputBytes.Length);
            compressionStream.Write(inputBytes);
        }

        var compressedString = Convert.ToBase64String(output.GetBuffer(), 0, checked((int)output.Length));
#else
        var span = inputBytes.AsSpan();
        using var result = Iron.Compress(Codec.Zstd, span);
        var compressedBytes = result.AsSpan();
        var compressedString = Convert.ToBase64String(compressedBytes);
#endif

        return new (compressedString);
    }

    /// <inheritdoc />
    public ValueTask<string> DecompressAsync(string input, CancellationToken cancellationToken = default)
    {
        var inputBytes = Convert.FromBase64String(input);
#if NET11_0_OR_GREATER
        if (inputBytes.Length == 0 || !ZstandardDecoder.TryGetMaxDecompressedLength(inputBytes, out var maxLength) || maxLength > Array.MaxLength)
            throw new InvalidDataException("The input is not a valid Zstandard payload or is too large to decompress.");

        var decompressedBytes = new byte[checked((int)maxLength)];
        // Unlike a streaming read, this rejects trailing data after a valid frame.
        if (!ZstandardDecoder.TryDecompress(inputBytes, decompressedBytes, out var bytesWritten))
            throw new InvalidDataException("The input is not a valid Zstandard payload.");

        var decompressedString = Encoding.UTF8.GetString(decompressedBytes, 0, bytesWritten);
#else
        var span = inputBytes.AsSpan();
        using var result = Iron.Decompress(Codec.Zstd, span);
        var decompressedBytes = result.AsSpan();
        var decompressedString = Encoding.UTF8.GetString(decompressedBytes);
#endif

        return new (decompressedString);
    }
}
