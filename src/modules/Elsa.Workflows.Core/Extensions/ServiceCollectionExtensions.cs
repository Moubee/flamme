using System.Diagnostics.CodeAnalysis;
using Elsa.Workflows;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Elsa.Extensions;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddStorageDriver<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IStorageDriver
        {
            return services.AddScoped<IStorageDriver, T>();
        }

        public IServiceCollection AddActivityStateFilter<T>() where T : class, IActivityStateFilter
        {
            return services.AddScoped<IActivityStateFilter, T>();
        }
    }
}