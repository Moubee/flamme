using Flamme.Http;
using Flamme.Workflows;
using JetBrains.Annotations;

namespace Flamme.Server.Web.Filters;

/// <summary>
/// Mask the value of the input if it is a HttpRequest and the input name is "Authorization".
/// </summary>
[UsedImplicitly]
public class HttpRequestAuthenticationHeaderFilter : ActivityStateFilterBase
{
    protected override ActivityStateFilterResult OnExecute(ActivityStateFilterContext context)
    {
        var activityExecutionContext = context.ActivityExecutionContext;
        var activity = activityExecutionContext.Activity;
        var inputDescriptor = context.InputDescriptor;

        if (activity is not SendHttpRequestBase || inputDescriptor.Name is not nameof(SendHttpRequestBase.Authorization))
            return ActivityStateFilterResult.Pass();
        
        var contextValue = context.Value.GetString();

        if (contextValue == null)
            return ActivityStateFilterResult.Pass();

        var maskedValue = new string('*', contextValue.Length);
        return Filtered(maskedValue);
    }
}