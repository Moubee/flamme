using JetBrains.Annotations;

namespace Flamme.Server.Web;

/// <summary>
/// Entry-point marker so a test can boot this host with <c>WebApplicationFactory</c>.
/// </summary>
/// <remarks>
/// The namespaced marker identifies the reference server for host smoke tests. Existing component tests
/// may continue to use the global <c>Program</c> entry point.
/// </remarks>
[UsedImplicitly]
public sealed class ClassicServerHost;
