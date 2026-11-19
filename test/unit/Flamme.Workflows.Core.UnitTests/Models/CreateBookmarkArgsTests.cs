using Flamme.Testing.Shared;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.Core.UnitTests.Models;

public class CreateBookmarkArgsTests
{
    [Test]
    public async Task NewCreateBookmarkArgs_IncludeActivityInstanceId_DefaultsToTrue()
    {
        var args = new CreateBookmarkArgs();

        await Assert.That(args.IncludeActivityInstanceId).IsTrue();
    }

    [Test]
    public async Task UnsetIncludeActivityInstanceId_InObjectInitializer_IsTrue()
    {
        var args = new CreateBookmarkArgs
        {
            Stimulus = "order-created"
        };

        await Assert.That(args.IncludeActivityInstanceId).IsTrue();
    }

    [Test]
    public async Task ExplicitFalse_IsPreserved()
    {
        var args = new CreateBookmarkArgs
        {
            Stimulus = "order-created",
            IncludeActivityInstanceId = false
        };

        await Assert.That(args.IncludeActivityInstanceId).IsFalse();
    }

    [Test]
    public async Task CreateBookmark_StimulusConvenience_MatchesParameterlessIdentityInclusion()
    {
        var context = await CreateContextAsync();

        var noArgs = context.CreateBookmark();
        var withStimulus = context.CreateBookmark("order-created");

        await Assert.That(noArgs.Hash).Contains(context.Id);
        await Assert.That(withStimulus.Hash).Contains(context.Id);
    }

    [Test]
    public async Task CreateBookmark_UnsetArgs_MatchesStimulusConvenienceHash()
    {
        var context = await CreateContextAsync();
        const string stimulus = "order-created";

        var fromConvenience = context.CreateBookmark(stimulus);
        var fromUnsetArgs = context.CreateBookmark(new CreateBookmarkArgs
        {
            Stimulus = stimulus
        });
        var fromCallbackOverload = context.CreateBookmark(stimulus, callback: null);

        await Assert.That(fromUnsetArgs.Hash).IsEqualTo(fromConvenience.Hash);
        await Assert.That(fromCallbackOverload.Hash).IsEqualTo(fromConvenience.Hash);
        await Assert.That(fromUnsetArgs.Hash).Contains(context.Id);
    }

    [Test]
    public async Task CreateBookmark_ExplicitFalse_ExcludesActivityInstanceIdFromHash()
    {
        var context = await CreateContextAsync();
        const string stimulus = "order-created";

        var included = context.CreateBookmark(stimulus);
        var excluded = context.CreateBookmark(new CreateBookmarkArgs
        {
            Stimulus = stimulus,
            IncludeActivityInstanceId = false
        });

        await Assert.That(included.Hash).Contains(context.Id);
        await Assert.That(excluded.Hash).DoesNotContain(context.Id);
        await Assert.That(excluded.Hash).IsNotEqualTo(included.Hash);
    }

    private static Task<ActivityExecutionContext> CreateContextAsync()
    {
        var fixture = new ActivityTestFixture(new WriteLine("test"));
        fixture.ConfigureServices(services =>
        {
            services.AddSingleton<IHasher, ConcatHasher>();
            services.AddSingleton<IIdentityGenerator, GuidIdentityGenerator>();
        });
        return fixture.BuildAsync();
    }

    /// <summary>
    /// Encodes hash inputs so tests can see whether the activity instance ID was passed through.
    /// </summary>
    private sealed class ConcatHasher : IHasher
    {
        public string Hash(string value) => value;

        public string Hash(params object?[] values) =>
            string.Join("|", values.Select(value => value?.ToString() ?? string.Empty));
    }
}
