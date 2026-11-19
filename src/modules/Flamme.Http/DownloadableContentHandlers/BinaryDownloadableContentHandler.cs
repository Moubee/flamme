using Flamme.Http.Abstractions;
using Flamme.Http.Contexts;

namespace Flamme.Http.DownloadableContentHandlers;

/// <summary>
/// Handles content that represents a downloadable binary file.
/// </summary>
public class BinaryDownloadableContentHandler : DownloadableContentHandlerBase
{
    /// <inheritdoc />
    public override bool GetSupportsContent(object content) => content is byte[];

    /// <inheritdoc />
    protected override Downloadable GetDownloadable(DownloadableContext context)
    {
        var bytes = (byte[]) context.Content;
#if NET11_0_OR_GREATER
        var stream = new ReadOnlyMemoryStream(bytes);
#else
        var stream = new MemoryStream(bytes, writable: false);
#endif
        var fileName = "file.bin";
        var contentType = "application/octet-stream";
        return new(stream, fileName, contentType);
    }
}
