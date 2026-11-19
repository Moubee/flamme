using Flamme.Dashboard.Abstractions.Contracts;
using Flamme.Dashboard.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Dashboard.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDashboardApiServices(this IServiceCollection services)
    {
        return services
            .AddScoped<DashboardRangeResolver>()
            .AddScoped<IDashboardProvider, DefaultDashboardProvider>();
    }
}
