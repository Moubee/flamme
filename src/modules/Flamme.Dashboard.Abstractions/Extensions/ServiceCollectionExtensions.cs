using Flamme.Dashboard.Abstractions.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Dashboard.Abstractions.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDashboardContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, IDashboardContributor
    {
        return services.AddScoped<IDashboardContributor, TContributor>();
    }
}
