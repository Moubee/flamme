using Microsoft.Extensions.Options;

namespace Flamme.Workflows.Management.Options;

internal sealed class ActivityBindingOptionsValidator : IValidateOptions<ManagementOptions>
{
    public ValidateOptionsResult Validate(string? name, ManagementOptions options)
    {
        foreach (var activityType in options.ActivityTypes)
            ActivityContextRegistry.GetRequiredBinding(activityType);

        return ValidateOptionsResult.Success;
    }
}
