using Flamme.Expressions.Python.Options;
using Flamme.Workflows;
using Flamme.Workflows.Models;
using Microsoft.Extensions.Options;

namespace Flamme.Expressions.Python.ActivityDescriptorModifiers;

internal class PythonActivityDescriptorModifier(IOptions<PythonOptions> options) : IActivityDescriptorModifier
{
    public void Modify(ActivityDescriptor descriptor)
    {
        if (descriptor.TypeName != WorkflowScriptActivityTypeNames.RunPython)
            return;

        descriptor.IsBrowsable = options.Value.AllowHostCodeExecution;
    }
}
