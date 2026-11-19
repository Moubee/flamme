using Flamme.Identity.Contracts;
using Flamme.Identity.Entities;
using Flamme.Identity.Models;
using Flamme.Identity.Options;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;

namespace Flamme.Identity.Providers;

/// <summary>
/// Represents an application provider that uses <see cref="ApplicationsOptions"/> to find applications.
/// </summary>
[PublicAPI]
public class ConfigurationBasedApplicationProvider : IApplicationProvider
{
    private readonly IOptions<ApplicationsOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationBasedApplicationProvider"/> class.
    /// </summary>
    public ConfigurationBasedApplicationProvider(IOptions<ApplicationsOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public Task<Application?> FindAsync(ApplicationFilter filter, CancellationToken cancellationToken = default)
    {
        var applicationsQueryable = _options.Value.Applications.AsQueryable();
        var application = filter.Apply(applicationsQueryable).FirstOrDefault();
        return Task.FromResult(application);
    }
}