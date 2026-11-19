using Flamme.Api.Client.Resources.Package.Models;
using Refit;

namespace Flamme.Api.Client.Resources.Package.Contracts;

/// <summary>
/// A client for working with packages.
/// </summary>
public interface IPackageApi
{
    /// <summary>
    /// Gets the installed package version of Flamme.
    /// </summary>
    [Get("/package/version")]
    Task<PackageVersionResponse> GetAsync(CancellationToken cancellationToken = default);   
}