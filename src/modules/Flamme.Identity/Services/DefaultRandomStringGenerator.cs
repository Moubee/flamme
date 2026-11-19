using System.Security.Cryptography;
using Flamme.Identity.Constants;
using Flamme.Identity.Contracts;

namespace Flamme.Identity.Services;

/// <inheritdoc />
public class DefaultRandomStringGenerator : IRandomStringGenerator
{
    /// <inheritdoc />
    public string Generate(int length = 32, char[]? chars = null)
    {
        chars ??= CharacterSequences.AlphanumericSequence;
        return RandomNumberGenerator.GetString(chars, length);
    }
}
