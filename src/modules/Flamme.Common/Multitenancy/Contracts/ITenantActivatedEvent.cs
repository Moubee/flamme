namespace Flamme.Common.Multitenancy;

public interface ITenantActivatedEvent
{
    Task TenantActivatedAsync(TenantActivatedEventArgs args);
}