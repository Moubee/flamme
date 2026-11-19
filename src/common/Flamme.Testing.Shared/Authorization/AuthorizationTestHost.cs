using Elsa.Authorization;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Testing.Shared.Authorization;

/// <summary>
/// A FastEndpoints host exercising real endpoint authorization: callers name themselves and their permissions in headers
/// (see <see cref="PermissionHeaderAuthenticationHandler"/>), and only the endpoints <c>endpointFilter</c> admits are mapped.
/// </summary>
/// <remarks>
/// Owns <see cref="EndpointSecurityOptions.SecurityIsEnabled"/> for its lifetime: it is pinned when the host starts and
/// restored on disposal. Endpoints read it when the host maps them, so a host with security disabled is built that way,
/// which is what <see cref="RestartWithSecurityDisabledAsync"/> does. The flag is process-global, so test classes using
/// this host must share a native keyed non-parallel constraint (declared per test assembly).
/// </remarks>
public sealed class AuthorizationTestHost : IAsyncDisposable
{
    private readonly bool _wasSecurityEnabled;
    private readonly Type _endpointAssemblyMarker;
    private readonly Func<Type, bool> _endpointFilter;
    private readonly Action<IServiceCollection>? _configureServices;
    private WebApplication _app;

    private AuthorizationTestHost(bool wasSecurityEnabled, Type endpointAssemblyMarker, Func<Type, bool> endpointFilter, Action<IServiceCollection>? configureServices, WebApplication app)
    {
        _wasSecurityEnabled = wasSecurityEnabled;
        _endpointAssemblyMarker = endpointAssemblyMarker;
        _endpointFilter = endpointFilter;
        _configureServices = configureServices;
        _app = app;
    }

    /// <summary>Starts a host, with security enabled, mapping the endpoints of the assembly declaring <typeparamref name="TAssemblyMarker"/> that pass <paramref name="endpointFilter"/>.</summary>
    public static async Task<AuthorizationTestHost> StartAsync<TAssemblyMarker>(Func<Type, bool> endpointFilter, Action<IServiceCollection>? configureServices = null)
    {
        var wasSecurityEnabled = EndpointSecurityOptions.SecurityIsEnabled;
        EndpointSecurityOptions.SecurityIsEnabled = true;

        WebApplication? app = null;
        try
        {
            app = await BuildAsync(typeof(TAssemblyMarker), endpointFilter, configureServices);
            await app.StartAsync();
            // The setting from before the host pinned it, so disposal restores what the caller had.
            return new(wasSecurityEnabled, typeof(TAssemblyMarker), endpointFilter, configureServices, app);
        }
        catch (Exception initializationFailure)
        {
            try
            {
                if (app is not null)
                    await app.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Authorization test host initialization and cleanup both failed.", initializationFailure, cleanupFailure);
            }
            finally
            {
                EndpointSecurityOptions.SecurityIsEnabled = wasSecurityEnabled;
            }
            throw;
        }
    }

    /// <summary>Replaces the host with an identically configured one built while security is disabled, the way a deployment disables it.</summary>
    public async Task RestartWithSecurityDisabledAsync()
    {
        await StopAsync();
        EndpointSecurityOptions.SecurityIsEnabled = false;
        _app = await BuildAsync(_endpointAssemblyMarker, _endpointFilter, _configureServices);
        await _app.StartAsync();
    }

    /// <summary>
    /// Sends a request. A request names a user, and so is authenticated, when <paramref name="authenticated"/> is set or
    /// <paramref name="permissions"/> (comma-separated grants) is given; otherwise it is anonymous.
    /// </summary>
    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? permissions = null, bool authenticated = false, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };

        if (permissions != null)
        {
            request.Headers.Add(PermissionHeaderAuthenticationHandler.PermissionsHeaderName, permissions);
        }

        if (authenticated || permissions != null)
        {
            request.Headers.Add(PermissionHeaderAuthenticationHandler.UserHeaderName, "test-user");
        }

        return _app.GetTestClient().SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        finally
        {
            EndpointSecurityOptions.SecurityIsEnabled = _wasSecurityEnabled;
        }
    }

    private async Task StopAsync()
    {
        try
        {
            await _app.StopAsync();
        }
        finally
        {
            await _app.DisposeAsync();
        }
    }

    private static async Task<WebApplication> BuildAsync(Type endpointAssemblyMarker, Func<Type, bool> endpointFilter, Action<IServiceCollection>? configureServices)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options =>
        {
            options.Assemblies = [endpointAssemblyMarker.Assembly];
            options.Filter = endpointFilter;
        });
        configureServices?.Invoke(builder.Services);
        builder.Services
            .AddAuthentication(PermissionHeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, PermissionHeaderAuthenticationHandler>(PermissionHeaderAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        try
        {
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseFastEndpoints();
            return app;
        }
        catch (Exception initializationFailure)
        {
            try
            {
                await app.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Authorization test host configuration and cleanup both failed.", initializationFailure, cleanupFailure);
            }
            throw;
        }
    }
}
