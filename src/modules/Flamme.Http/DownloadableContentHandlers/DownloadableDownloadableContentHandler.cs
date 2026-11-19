using Flamme.Http.Abstractions;
using Flamme.Http.Contexts;

namespace Flamme.Http.DownloadableContentHandlers;

/// <summary>
/// Handles content that represents a downloadable.
/// </summary>
public class DownloadableDownloadableContentHandler : DownloadableContentHandlerBase
{
    /// <inheritdoc />
    public override bool GetSupportsContent(object content) => content is Downloadable;

    /// <inheritdoc />
    protected override Downloadable GetDownloadable(DownloadableContext context)
    {
        return (Downloadable) context.Content;
    }
}