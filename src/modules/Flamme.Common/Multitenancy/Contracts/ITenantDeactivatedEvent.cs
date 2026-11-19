namespace Flamme.Common.Multitenancy;

public interface ITenantDeactivatedEvent
{
    Task TenantDeactivatedAsync(TenantDeactivatedEventArgs args);
}