using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows;

/// <summary>
/// Activates an unregistered activity-backed default provider from generated constructor bindings.
/// </summary>
public static class ActivityProviderActivation
{
    public sealed record Parameter(Type Type, object? Key, bool HasDefault, object? DefaultValue);

    public sealed record Constructor(
        bool Preferred,
        IReadOnlyList<Parameter> Parameters,
        Func<object?[], IActivityPropertyDefaultValueProvider> Invoke);

    public static IActivityPropertyDefaultValueProvider Create(
        IServiceProvider services,
        Type provider,
        IReadOnlyList<Constructor> constructors)
    {
        var inspector = services.GetService<IServiceProviderIsService>();
        var (constructor, defaults) = Select(provider, constructors, inspector);
        var arguments = defaults ?? new object?[constructor.Parameters.Count];
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] != null)
                continue;

            var parameter = constructor.Parameters[index];
            arguments[index] = Resolve(services, parameter)
                ?? (parameter.HasDefault
                    ? parameter.DefaultValue
                    : throw new InvalidOperationException($"Unable to resolve service for type '{parameter.Type}' while attempting to activate '{provider}'."));
        }

        return constructor.Invoke(arguments);
    }

    private static (Constructor Constructor, object?[]? Defaults) Select(
        Type provider,
        IReadOnlyList<Constructor> constructors,
        IServiceProviderIsService? inspector)
    {
        var preferred = constructors.Where(constructor => constructor.Preferred).ToList();
        if (preferred.Count > 1)
            throw new InvalidOperationException($"Multiple constructors on '{provider}' are marked with ActivatorUtilitiesConstructorAttribute.");

        if (preferred.Count == 1)
        {
            var candidate = preferred[0];
            if (inspector == null)
                return (candidate, null);

            var defaults = MatchDefaults(inspector, candidate)
                ?? throw new InvalidOperationException($"The constructor on '{provider}' marked with ActivatorUtilitiesConstructorAttribute could not be satisfied.");
            return (candidate, defaults);
        }

        if (inspector != null)
        {
            Constructor? best = null;
            object?[]? bestDefaults = null;
            var ambiguous = false;
            foreach (var candidate in constructors)
            {
                var defaults = MatchDefaults(inspector, candidate);
                if (defaults == null)
                    continue;

                if (best == null || candidate.Parameters.Count > best.Parameters.Count)
                {
                    best = candidate;
                    bestDefaults = defaults;
                    ambiguous = false;
                }
                else if (candidate.Parameters.Count == best.Parameters.Count)
                    ambiguous = true;
            }

            if (ambiguous)
                throw Ambiguous(provider);

            if (best != null)
                return (best, bestDefaults);
        }

        // With no explicit arguments every public constructor is a candidate if
        // service availability could not identify a match.
        return constructors.Count switch
        {
            1 => (constructors[0], null),
            0 => throw new InvalidOperationException($"No public constructor was found for activity-backed default provider '{provider}'."),
            _ => throw Ambiguous(provider)
        };
    }

    private static object?[]? MatchDefaults(IServiceProviderIsService inspector, Constructor constructor)
    {
        var arguments = new object?[constructor.Parameters.Count];
        for (var index = 0; index < arguments.Length; index++)
        {
            var parameter = constructor.Parameters[index];
            if (IsService(inspector, parameter))
                continue;
            if (!parameter.HasDefault)
                return null;
            arguments[index] = parameter.DefaultValue;
        }
        return arguments;
    }

    private static bool IsService(IServiceProviderIsService inspector, Parameter parameter) =>
        parameter.Key == null
            ? inspector.IsService(parameter.Type)
            : inspector is IServiceProviderIsKeyedService keyed
                ? keyed.IsKeyedService(parameter.Type, parameter.Key)
                : throw new InvalidOperationException("The service provider does not support keyed services.");

    private static object? Resolve(IServiceProvider services, Parameter parameter) =>
        parameter.Key == null
            ? services.GetService(parameter.Type)
            : services is IKeyedServiceProvider keyed
                ? keyed.GetKeyedService(parameter.Type, parameter.Key)
                : throw new InvalidOperationException("The service provider does not support keyed services.");

    private static InvalidOperationException Ambiguous(Type provider)
        => new($"Multiple constructors accepting all given argument types were found in type '{provider}'. There should only be one applicable constructor.");
}
