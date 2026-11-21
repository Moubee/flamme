using System.Linq.Expressions;
using System.Reflection;
using Flamme.Expressions.Helpers;
using Flamme.Expressions.Models;
using Flamme.Workflows;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Models;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

/// <summary>
/// Provides extension methods for <see cref="IActivity"/>.
/// </summary>
[PublicAPI]
public static class ActivityExtensions
{
    /// <param name="activity">The activity to get the output from.</param>
    extension(IActivity activity)
    {
        /// <summary>
        /// Gets the input properties of the specified activity.
        /// </summary>
        public IDictionary<string, Input> GetNamedInputs()
        {
            return ActivityContextRegistry.GetRequiredBinding(activity.GetType()).Inputs
                .Where(x => typeof(Input).IsAssignableFrom(x.PropertyType))
                .Select(x => (x.ClrName, Value: (Input?)x.GetValue(activity)))
                .Where(x => x.Value != null)
                .DistinctBy(x => x.ClrName)
                .ToDictionary(x => x.ClrName, x => x.Value!);
        }

        /// <summary>
        /// Gets the input properties of the specified activity.
        /// </summary>
        public IEnumerable<Input> GetInputs() => GetNamedInputs(activity).Values;

        /// <summary>
        /// Gets the input with the specified name.
        /// </summary>
        public Input? GetInput(string inputName) => GetNamedInputs(activity).TryGetValue(inputName, out var input) ? input : null;

        /// <summary>
        /// Gets the output properties of the specified activity.
        /// </summary>
        public IEnumerable<NamedOutput> GetOutputs()
        {
            var binding = ActivityContextRegistry.GetRequiredBinding(activity.GetType());
            return binding.Outputs
                .Select(x => (x.ClrName, Value: (Output?)x.GetValue(activity)))
                .Where(x => x.Value != null)
                .Select(x => new NamedOutput(x.ClrName, x.Value!))
                .ToList();
        }

        /// <summary>
        /// Gets the output with the specified name.
        /// </summary>
        /// <param name="context">The activity execution context.</param>
        /// <param name="outputName">Name of the output.</param>
        /// <returns>The output value.</returns>
        public object? GetOutput(ActivityExecutionContext context, string? outputName = null)
        {
            var workflowExecutionContext = context.WorkflowExecutionContext;
            var outputRegister = workflowExecutionContext.GetActivityOutputRegister();
        
            // If the provided activity execution context is the same as the current activity's execution context, we return the exact output value of the current activity execution context.
            if(context.Activity.NodeId == activity.NodeId)
                return outputRegister.FindOutputByActivityInstanceId(context.Id, outputName);
        
            // If the provided activity execution context is different from the current activity's execution context, we look for the last output value of the activity.
            return outputRegister.FindOutputByActivityId(activity.Id, outputName);
        }

        /// <summary>
        /// Gets the output with the specified name.
        /// </summary>
        /// <param name="context">The expression execution context.</param>
        /// <param name="outputName">Name of the output.</param>
        /// <returns>The output value.</returns>
        public object? GetOutput(ExpressionExecutionContext context, string? outputName = null)
        {
            var activityExecutionContext = context.GetActivityExecutionContext();

            if (activityExecutionContext == null)
                return null;
        
            return activity.GetOutput(activityExecutionContext, outputName);
        }

        /// <summary>
        /// Gets the output with the specified name.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="outputName">Name of the output.</param>
        /// <typeparam name="T">The type of the output.</typeparam>
        /// <returns>The output value.</returns>
        public T? GetOutput<T>(ActivityExecutionContext context, string outputName)
        {
            var outputValue = activity.GetOutput(context, outputName);
            return outputValue == null ? default! : outputValue.ConvertTo<T>();
        }

        /// <summary>
        /// Gets the output with the specified name.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="outputName">Name of the output.</param>
        /// <typeparam name="T">The type of the output.</typeparam>
        /// <returns>The output value.</returns>
        public T? GetOutput<T>(ExpressionExecutionContext context, string outputName)
        {
            return activity.GetOutput<T>(context.GetActivityExecutionContext(), outputName);
        }
    }

    /// <param name="activity">The activity.</param>
    /// <typeparam name="TActivity">The type of the activity.</typeparam>
    extension<TActivity>(TActivity activity)
    {
        /// <summary>
        /// Gets the output with the specified name.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="outputExpression">The output expression.</param>
        /// <typeparam name="T">The type of the output.</typeparam>
        /// <returns>The output value.</returns>
        public T? GetOutput<T>(ActivityExecutionContext context, Expression<Func<TActivity, object?>> outputExpression)
        {
            var outputName = outputExpression.GetPropertyName();
            return ((IActivity)activity!).GetOutput<T>(context, outputName);
        }

        /// <summary>
        /// Gets the output with the specified name.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="outputExpression">The output expression.</param>
        /// <typeparam name="T">The type of the output.</typeparam>
        /// <returns>The output value.</returns>
        public T? GetOutput<T>(ExpressionExecutionContext context, Expression<Func<TActivity, object?>> outputExpression)
        {
            var outputName = outputExpression.GetPropertyName();
            return ((IActivity)activity!).GetOutput<T>(context, outputName);
        }
    }

    /// <param name="activity">The activity.</param>
    extension(IActivity activity)
    {
        /// <summary>
        /// Gets the Result output of the specified activity.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <typeparam name="T">The type as which to return the output.</typeparam>
        /// <returns>The output value.</returns>
        public T? GetResult<T>(ExpressionExecutionContext context)
        {
            var value = GetResult(activity, context);
            return value == null ? default! : value.ConvertTo<T>();
        }

        /// <summary>
        /// Gets the Result output of the specified activity.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>The output value.</returns>
        public object? GetResult(ExpressionExecutionContext context)
        {
            return activity.GetResult(context.GetActivityExecutionContext());
        }

        /// <summary>
        /// Gets the Result output of the specified activity.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>The output value.</returns>
        public object? GetResult(ActivityExecutionContext context)
        {
            return activity.GetOutput(context, ActivityOutputRegister.DefaultOutputName);
        }
    }

    /// <summary>
    /// Gets the Result output of the specified activity.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="activity">The activity.</param>
    /// <returns>The output value.</returns>
    public static object? GetResult(this ActivityExecutionContext context, IActivity activity)
    {
        return activity.GetOutput(context, ActivityOutputRegister.DefaultOutputName);
    }

    /// <summary>
    /// Gets the result of the last activity.
    /// </summary>
    /// <param name="context">The context.</param>
    public static T? GetLastResult<T>(this ExpressionExecutionContext context)
    {
        var value = GetLastResult(context.GetWorkflowExecutionContext());
        return value == null ? default! : value.ConvertTo<T>();
    }

    /// <summary>
    /// Gets the result of the last activity.
    /// </summary>
    /// <param name="context">The context.</param>
    public static object? GetLastResult(this ActivityExecutionContext context)
    {
        return context.WorkflowExecutionContext.GetLastResult();
    }
    
    /// <summary>
    /// Gets the result of the last activity.
    /// </summary>
    /// <param name="context">The context.</param>
    public static object? GetLastResult(this WorkflowExecutionContext context)
    {
        return context.GetLastActivityResult();
    }

    extension(IActivity activity)
    {
        /// <summary>
        /// Gets the input properties of the specified activity.
        /// </summary>
        public IEnumerable<PropertyInfo> GetInputProperties() => activity.GetType().GetProperties().Where(x => typeof(Input).IsAssignableFrom(x.PropertyType)).ToList();

        /// <summary>
        /// Gets the method for the specified method name on the specified activity.
        /// </summary>
        public TDelegate GetDelegate<TDelegate>(string methodName) where TDelegate : Delegate
        {
            if (typeof(TDelegate) != typeof(ExecuteActivityDelegate) && typeof(TDelegate) != typeof(ActivityCompletionCallback))
                return BindCustomDelegate<TDelegate>(activity, methodName);

            var activityType = activity.GetType();
            var binding = ActivityContextRegistry.GetRequiredBinding(activityType);
            Delegate? callback = typeof(TDelegate) == typeof(ExecuteActivityDelegate)
                ? binding.BindBookmarkCallback(activity, methodName)
                : binding.BindCompletionCallback(activity, methodName);

            if (callback is TDelegate typedCallback)
                return ActivityCallbackNames.Remember(typedCallback, methodName);

            if (methodName.StartsWith('<'))
                throw ActivityCallbackNames.UnsupportedCompilerCallback(activityType, methodName);

            throw ActivityContextRegistry.MissingBinding(activityType, $"{typeof(TDelegate).Name} callback '{methodName}'");
        }

        /// <summary>
        /// Gets the Resume method for the specified activity.
        /// </summary>
        public ExecuteActivityDelegate GetResumeActivityDelegate(string resumeMethodName) => activity.GetDelegate<ExecuteActivityDelegate>(resumeMethodName);

        /// <summary>
        /// Gets the Child Activity Completed method for the specified activity.
        /// </summary>
        public ActivityCompletionCallback GetActivityCompletionCallback(string completionMethodName) => activity.GetDelegate<ActivityCompletionCallback>(completionMethodName);
    }

    private static TDelegate BindCustomDelegate<TDelegate>(IActivity activity, string methodName) where TDelegate : Delegate
    {
        var activityType = activity.GetType();
        const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        var resumeMethodInfo = default(MethodInfo?);
        var currentType = activityType;

        while (currentType != null && resumeMethodInfo == null)
        {
            resumeMethodInfo = currentType.GetMethod(methodName, bindingFlags);
            currentType = currentType.BaseType;
        }

        if (resumeMethodInfo == null)
            throw new Exception($"Can't find method name {methodName} on type {activityType} or its base type {activityType.BaseType}");

        return resumeMethodInfo.IsStatic ? (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), resumeMethodInfo) : (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), activity, resumeMethodInfo);
    }
}
