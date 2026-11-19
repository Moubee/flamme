using Flamme.Hosting.Management.Contracts;

namespace Flamme.Hosting.Management.Services;

/// <summary>
/// Returns a randomly generated instance name.
/// </summary>
public class RandomApplicationInstanceNameProvider(RandomIntIdentityGenerator identityGenerator) : IApplicationInstanceNameProvider
{
    private readonly string _instanceName = identityGenerator.GenerateId();

    /// <inheritdoc />
    public string GetName() => _instanceName;
}