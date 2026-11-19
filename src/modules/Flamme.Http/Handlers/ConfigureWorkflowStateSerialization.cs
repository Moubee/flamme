using System.Text.Json;
using Flamme.Http.Serialization;
using Flamme.Workflows;
using Flamme.Workflows.State;

namespace Flamme.Http.Handlers;

/// <summary>
/// Configures the serialization of <see cref="WorkflowState"/> objects.
/// </summary>
public class ConfigureWorkflowStateSerialization : SerializationOptionsConfiguratorBase
{
    public override void Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new HttpStatusCodeCaseForWorkflowInstanceConverter());
    }
}