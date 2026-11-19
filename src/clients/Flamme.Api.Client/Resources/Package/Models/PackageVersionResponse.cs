namespace Flamme.Api.Client.Resources.Package.Models;

/// <summary>
/// Contains the installed package version of Flamme.
/// </summary>
/// <param name="PackageVersion">The installed package version of Flamme.</param>
public record PackageVersionResponse(string PackageVersion);