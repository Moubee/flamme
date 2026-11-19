using Flamme.Http.Bookmarks;
using JetBrains.Annotations;

namespace Flamme.Http.Contexts;

[UsedImplicitly]
public record HttpEndpointRouteProviderContext(HttpEndpointBookmarkPayload Payload, string? TenantId, CancellationToken CancellationToken);