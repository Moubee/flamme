using Flamme.Workflows;
using Flamme.Workflows.Models;
using Flamme.Workflows.Options;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Testing.Shared;

/// <summary>
/// Provides extension methods for <see cref="IServiceProvider"/>.
/// </summary>
[PublicAPI]
[Obsolete("Use WorkflowTestFixture instead.")]
public static class RunActivityExtensions
{
    /// <param name="services">The service provider.</param>
    extension(IServiceProvider services)
    {
        /// <summary>
        /// Runs the specified activity.
        /// </summary>
        /// <param name="activity">The activity to run.</param>
        /// <param name="cancellationToken">An optional cancellation token.</param>
        /// <returns>The result of running the activity.</returns>
        [Obsolete("Use WorkflowTestFixture instead.")]
        public async Task<RunWorkflowResult> RunActivityAsync(IActivity activity, CancellationToken cancellationToken = default)
        {
            await services.PopulateRegistriesAsync();
            var workflowRunner = services.GetRequiredService<IWorkflowRunner>();
            var result = await workflowRunner.RunAsync(activity, cancellationToken: cancellationToken);
            return result;
        }

        /// <summary>
        /// Runs the specified activity.
        /// </summary>
        /// <param name="activity">The activity to run.</param>
        /// <param name="options">A set of options.</param>
        /// <param name="cancellationToken">An optional cancellation token.</param>
        /// <returns>The result of running the activity.</returns>
        [Obsolete("Use WorkflowTestFixture instead.")]
        public async Task<RunWorkflowResult> RunActivityAsync(IActivity activity, RunWorkflowOptions options, CancellationToken cancellationToken = default)
        {
            await services.PopulateRegistriesAsync();
            var workflowRunner = services.GetRequiredService<IWorkflowRunner>();
            var result = await workflowRunner.RunAsync(activity, options, cancellationToken);
            return result;
        }
    }
}