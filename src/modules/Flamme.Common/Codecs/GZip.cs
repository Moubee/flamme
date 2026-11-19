using System.IO.Compression;
using System.Text;

namespace Flamme.Common.Codecs;

/// <summary>
/// Represents a GZip compression strategy.
/// </summary>
public class GZip : ICompressionCodec
{
    /// <inheritdoc />
    public async ValueTask<string> CompressAsync(string input, CancellationToken cancellationToken)
    {
#if !NET11_0_OR_GREATER
        if (input is { Length: 0 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Before .NET 11, disposing an empty GZipStream does not emit a frame.
            return "H4sIAAAAAAAC/wMAAAAAAAAAAAA=";
        }
#endif
        using var output = new MemoryStream();
        await using (var compressionStream = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
#if NET11_0_OR_GREATER
            using var inputStream = new StringStream(input, Encoding.UTF8);
            await inputStream.CopyToAsync(compressionStream, cancellationToken);
#else
            var inputBytes = Encoding.UTF8.GetBytes(input);
            await compressionStream.WriteAsync(inputBytes, cancellationToken);
#endif
        }

        // Disposing the compressor writes the footer before the payload is read.
        return Convert.ToBase64String(output.ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<string> DecompressAsync(string input, CancellationToken cancellationToken)
    {
        var inputBytes = Convert.FromBase64String(input);
#if NET11_0_OR_GREATER
        using var inputStream = new ReadOnlyMemoryStream(inputBytes);
#else
        using var inputStream = new MemoryStream(inputBytes, writable: false);
#endif
        await using var decompressionStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputMemoryStream = new MemoryStream();
        await decompressionStream.CopyToAsync(outputMemoryStream, cancellationToken);
        var decompressedBytes = outputMemoryStream.ToArray();

        return Encoding.UTF8.GetString(decompressedBytes);
    }
}
