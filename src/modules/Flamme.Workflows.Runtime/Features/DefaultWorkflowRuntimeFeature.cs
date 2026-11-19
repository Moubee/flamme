using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;

namespace Flamme.Workflows.Runtime.Features;

/// <summary>
/// Installs the default runtime services.
/// </summary>
[DependsOn(typeof(WorkflowRuntimeFeature))]
public class DefaultWorkflowRuntimeFeature(IModule module) : FeatureBase(module);