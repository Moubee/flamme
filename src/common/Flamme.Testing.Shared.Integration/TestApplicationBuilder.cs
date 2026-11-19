using Flamme.Extensions;
using Flamme.Features.Services;
using Flamme.Workflows;
using Flamme.Workflows.Activities;
using FluentStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TUnit.Logging.Microsoft;

namespace Flamme.Testing.Shared;

/// <summary>
/// A builder that can be used to configure an <see cref="IServiceProvider"/> for testing purposes.
/// </summary>
public class TestApplicationBuilder
{
    private readonly TextWriter _testOutput;
    private readonly ServiceCollection _services;
    private Action<IModule> _configureFlamme;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestApplicationBuilder"/> class.
    /// </summary>
    /// <param name="testOutput">The writer that receives workflow output.</param>
    public TestApplicationBuilder(TextWriter testOutput)
    {
        ArgumentNullException.ThrowIfNull(testOutput);
        _testOutput = TextWriter.Synchronized(testOutput);
        _services = new();

        _services
            .AddSingleton(_testOutput)
            .AddSingleton<IConfiguration, ConfigurationManager>()
            .AddLogging(logging => logging
                .SetMinimumLevel(LogLevel.Debug)
                .AddCorrelatedTUnit(LogLevel.Debug));

        _configureFlamme += flamme => flamme
            .AddActivitiesFrom<WriteLine>()
            .UseScheduling()
            .UseCSharp(options => options.AllowHostCodeExecution = true)
            .UseJavaScript()
            .UseLiquid()
            .UseWorkflowManagement()
            .UseWorkflows(workflows => workflows
                .WithStandardOutStreamProvider(_ => new StandardOutStreamProvider(_testOutput))
            );
    }

    /// <summary>
    /// Builds the <see cref="IServiceProvider"/>.
    /// </summary>
    /// <returns>The <see cref="IServiceProvider"/>.</returns>
    public IServiceProvider Build()
    {
        _services.AddFlamme(_configureFlamme);
        return _services.BuildServiceProvider();
    }

    /// <summary>
    /// Configures Flamme.
    /// </summary>
    /// <param name="configure">The configuration delegate.</param>
    /// <returns>The <see cref="TestApplicationBuilder"/>.</returns>
    public TestApplicationBuilder ConfigureFlamme(Action<IModule> configure)
    {
        _configureFlamme += configure;
        return this;
    }

    /// <summary>
    /// Configures the service provider.
    /// </summary>
    /// <param name="configure">The configuration delegate.</param>
    /// <returns>The <see cref="TestApplicationBuilder"/>.</returns>
    public TestApplicationBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        configure(_services);
        return this;
    }

    /// <summary>
    /// Configures the service provider to use a <see cref="CapturingTextWriter"/> to capture standard output.
    /// </summary>
    /// <param name="capturingTextWriter">The capturing text writer.</param>
    /// <returns>The <see cref="TestApplicationBuilder"/>.</returns>
    public TestApplicationBuilder WithCapturingTextWriter(CapturingTextWriter capturingTextWriter)
    {
        var combinedTextWriter = TextWriter.Synchronized(new CombinedTextWriter(capturingTextWriter, _testOutput));
        var provider = new StandardOutStreamProvider(combinedTextWriter);

        ConfigureFlamme(flamme => flamme.UseWorkflows(workflows => workflows.WithStandardOutStreamProvider(_ => provider)));
        return this;
    }

    /// <summary>
    /// Adds a workflow to the service provider.
    /// </summary>
    public TestApplicationBuilder AddWorkflow<T>() where T : IWorkflow
    {
        return ConfigureFlamme(flamme => flamme.AddWorkflow<T>());
    }
    
    /// <summary>
    /// Adds activities from the assembly containing the specified type.
    /// </summary>
    public TestApplicationBuilder AddActivitiesFrom<T>()
    {
        return ConfigureFlamme(flamme => flamme.AddActivitiesFrom<T>());
    }
    
    /// <summary>
    /// Add workflows from the specified relative directory.
    /// </summary>
    /// <param name="directory">The path segments of the directory.</param>
    public TestApplicationBuilder WithWorkflowsFromDirectory(params string[] directory)
    {
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation)!;
        var workflowsDirectory = directory.Prepend(assemblyDirectory).ToArray();
        _configureFlamme += flamme => flamme.UseFluentStorageProvider(storage => storage.BlobStorage = sp => StorageFactory.Blobs.DirectoryFiles(Path.Combine(workflowsDirectory)));
        return this;
    }
}
