using Flamme.Identity.Constants;
using Flamme.Identity.Contracts;
using Flamme.Identity.Models;

namespace Flamme.Identity.Services;

/// <inheritdoc />
public class DefaultClientIdGenerator : IClientIdGenerator
{
    private readonly IRandomStringGenerator _randomStringGenerator;
    private readonly IApplicationProvider _applicationProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultClientIdGenerator"/> class.
    /// </summary>
    public DefaultClientIdGenerator(IRandomStringGenerator randomStringGenerator, IApplicationProvider applicationProvider)
    {
        _randomStringGenerator = randomStringGenerator;
        _applicationProvider = applicationProvider;
    }

    /// <inheritdoc />
    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var clientId = _randomStringGenerator.Generate(16, CharacterSequences.AlphanumericSequence);
            var filter = new ApplicationFilter { ClientId = clientId };
            var application = await _applicationProvider.FindAsync(filter, cancellationToken);

            if (application == null)
                return clientId;
        }
    }
}