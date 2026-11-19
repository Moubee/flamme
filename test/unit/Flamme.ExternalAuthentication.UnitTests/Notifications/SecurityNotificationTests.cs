using System.Diagnostics.CodeAnalysis;
using Elsa.ExternalAuthentication.Models;
using Elsa.ExternalAuthentication.Notifications;
using System.Threading.Tasks;

namespace Elsa.ExternalAuthentication.UnitTests.Notifications;

public class SecurityNotificationTests
{
    [Test]
    [Arguments(typeof(IdentityProviderConnectionChanged))]
    [Arguments(typeof(IdentityProviderConnectionLifecycleChanged))]
    [Arguments(typeof(IdentityProviderConnectionSecretBindingChanged))]
    [Arguments(typeof(IdentityProviderConnectionTested))]
    [Arguments(typeof(IdentityProviderConnectionPreviewed))]
    [Arguments(typeof(ExternalIdentityLinkChanged))]
    [Arguments(typeof(ExternalIdentityLinkReplaced))]
    [Arguments(typeof(ExternalAuthenticationSessionRevoked))]
    [Arguments(typeof(ExternalAuthenticationConnectionSessionsRevoked))]
    [Arguments(typeof(ExternalSignInCompleted))]
    [Arguments(typeof(ExternalAuthenticationOutcomeRecorded))]
    public async Task EventFamiliesAreImmutableAndCarryOnlyTheSharedSafeContext(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type notificationType)
    {
        var properties = notificationType.GetProperties();
        await Assert.That(properties).Contains(property => property.Name == "Context" && property.PropertyType == typeof(SecurityEventContext));
        await Assert.That(properties).DoesNotContain(property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Subject", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task SensitiveStringNeverFormatsItsValueForNotificationsOrLogs()
    {
        using var value = new SensitiveString("a-secret-value");
        await Assert.That(value.ToString()).IsEqualTo("[REDACTED]");
    }
}
