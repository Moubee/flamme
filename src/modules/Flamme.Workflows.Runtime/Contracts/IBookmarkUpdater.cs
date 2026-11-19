using Flamme.Workflows.Runtime.Requests;

namespace Flamme.Workflows.Runtime;

public interface IBookmarkUpdater
{
    Task UpdateBookmarksAsync(UpdateBookmarksRequest request, CancellationToken cancellationToken = default);
}