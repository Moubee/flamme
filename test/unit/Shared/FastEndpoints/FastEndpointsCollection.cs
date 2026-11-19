namespace Flamme.UnitTests.Shared;

/// <summary>
/// Serializes the test classes of an assembly that depend on FastEndpoints' process-wide state: the service resolver
/// and <c>EndpointSecurityOptions.SecurityIsEnabled</c>. A host writes both while it is built and <c>Factory.Create</c>
/// reads the resolver, so running two such classes concurrently lets one class's host leak into, or be disposed under,
/// another class's test.
///
/// Every host and factory consumer uses <c>[NotInParallel(nameof(FastEndpointsCollection))]</c>, which
/// serializes exactly this shared-state boundary. A class that builds a host also calls
/// <see cref="FastEndpointsResolver.Reset"/> when it disposes that host.
/// </summary>
public class FastEndpointsCollection;
