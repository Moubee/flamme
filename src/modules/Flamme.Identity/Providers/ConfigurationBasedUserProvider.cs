using Flamme.Identity.Contracts;
using Flamme.Identity.Entities;
using Flamme.Identity.Models;
using Flamme.Identity.Options;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;

namespace Flamme.Identity.Providers;

/// <summary>
/// Represents a user provider that finds users using <see cref="UsersOptions"/>.
/// </summary>
[PublicAPI]
public class ConfigurationBasedUserProvider : IUserProvider
{
    private readonly IOptions<UsersOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationBasedUserProvider"/> class.
    /// </summary>
    public ConfigurationBasedUserProvider(IOptions<UsersOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public Task<User?> FindAsync(UserFilter filter, CancellationToken cancellationToken = default)
    {
        var usersQueryable = _options.Value.Users.AsQueryable();
        var user = filter.Apply(usersQueryable).FirstOrDefault();
        return Task.FromResult(user);
    }
}