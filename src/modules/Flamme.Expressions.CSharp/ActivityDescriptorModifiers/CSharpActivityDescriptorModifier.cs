using Flamme.Expressions.CSharp.Options;
using Flamme.Workflows;
using Flamme.Workflows.Models;
using Microsoft.Extensions.Options;

namespace Flamme.Expressions.CSharp.ActivityDescriptorModifiers;

internal class CSharpActivityDescriptorModifier(IOptions<CSharpOptions> options) : IActivityDescriptorModifier
{
    public void Modify(ActivityDescriptor descriptor)
    {
        if (descriptor.TypeName != WorkflowScriptActivityTypeNames.RunCSharp)
            return;

        descriptor.IsBrowsable = options.Value.AllowHostCodeExecution;
    }
}
