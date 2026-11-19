using Flamme.Abstractions;
using Flamme.Features.Contracts;
using Flamme.Features.Models;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.Features.Get;

/// <summary>
/// Returns the specified installed feature.
/// </summary>
[PublicAPI]
internal class Get(IInstalledFeatureProvider installedFeatureProvider) : FlammeEndpointWithoutRequest<FeatureDescriptor>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/features/installed/{fullName}");

        // Clients such as Studio decide which modules to render from the installed features, so every signed-in user needs it.
        RequireAuthenticatedOnly();
    }

    /// <inheritdoc />
    public override async Task HandleAsync( CancellationToken cancellationToken)
    {
        var fullName = Route<string>("fullName")!;
        var descriptor = installedFeatureProvider.Find(fullName);

        if (descriptor == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(descriptor, cancellationToken);
    }
}