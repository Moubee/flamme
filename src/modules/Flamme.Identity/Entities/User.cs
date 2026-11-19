using Flamme.Common.Entities;

namespace Flamme.Identity.Entities;

/// <summary>
/// Represents a user.
/// </summary>
public class User : Entity
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets the hashed password.
    /// </summary>
    public string? HashedPassword { get; set; }

    /// <summary>
    /// Gets or sets the hashed password salt.
    /// </summary>
    public string? HashedPasswordSalt { get; set; }

    /// <summary>
    /// Gets or sets the roles.
    /// </summary>
    public ICollection<string> Roles { get; set; } = new List<string>();
}
