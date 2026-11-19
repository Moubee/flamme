using Flamme.Identity.Constants;
using Flamme.Identity.Contracts;

namespace Flamme.Identity.Services;

/// <inheritdoc />
public class DefaultSecretGenerator : ISecretGenerator
{
    private readonly IRandomStringGenerator _randomStringGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultSecretGenerator"/> class.
    /// </summary>
    /// <param name="randomStringGenerator"></param>
    public DefaultSecretGenerator(IRandomStringGenerator randomStringGenerator)
    {
        _randomStringGenerator = randomStringGenerator;
    }
    
    /// <inheritdoc />
    public string Generate(int length = 32) => _randomStringGenerator.Generate(length, CharacterSequences.AlphanumericAndSymbolSequence);
}