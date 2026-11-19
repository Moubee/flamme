using Flamme.Workflows.Runtime.Requests;

namespace Flamme.Workflows.Runtime;

/// <summary>
/// Persists bookmarks and raises events.
/// </summary>
public interface IBookmarksPersister
{
    /// <summary>
    /// Persists bookmarks and raises events.
    /// </summary>
    Task PersistBookmarksAsync(UpdateBookmarksRequest updateBookmarksRequest);
}