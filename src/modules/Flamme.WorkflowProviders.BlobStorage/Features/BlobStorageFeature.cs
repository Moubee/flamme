using System.Reflection;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.WorkflowProviders.BlobStorage.Contracts;
using Flamme.WorkflowProviders.BlobStorage.Handlers;
using Flamme.WorkflowProviders.BlobStorage.Providers;
using Flamme.Workflows.Management.Features;
using FluentStorage;
using FluentStorage.Blobs;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.WorkflowProviders.BlobStorage.Features;

/// <summary>
/// A feature that enables the FluentStorage workflow definition provider.
/// </summary>
[DependsOn(typeof(WorkflowManagementFeature))]
public class BlobStorageFeature : FeatureBase
{
    /// <inheritdoc />
    public BlobStorageFeature(IModule module) : base(module)
    {
    }

    /// <summary>
    /// The blob storage to use.
    /// </summary>
    public Func<IServiceProvider, IBlobStorage> BlobStorage { get; set; } = _ => StorageFactory.Blobs.DirectoryFiles(GetDefaultWorkflowsDirectory());

    /// <inheritdoc />
    public override void Apply()
    {
        Services.AddScoped<IBlobStorageProvider>(sp => new BlobStorageProvider(BlobStorage(sp)));

        // Register the JSON format handler (built-in support)
        Services.AddScoped<IBlobWorkflowFormatHandler, JsonBlobWorkflowFormatHandler>();

        Services.AddWorkflowsProvider<BlobStorageWorkflowsProvider>();
    }

    /// <summary>
    /// Gets the default workflows directory.
    /// </summary>
    /// <returns>The default workflows directory.</returns>
    public static string GetDefaultWorkflowsDirectory()
    {
        var entryAssemblyDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!;
        var directory = Path.Combine(entryAssemblyDir, "Workflows");
        return directory;
    }

}