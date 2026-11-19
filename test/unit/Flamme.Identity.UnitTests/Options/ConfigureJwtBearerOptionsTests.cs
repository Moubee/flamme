using Flamme.Extensions;
using Flamme.Identity.Constants;
using Flamme.Identity.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Threading.Tasks;

namespace Flamme.Identity.UnitTests.Options;

public class ConfigureJwtBearerOptionsTests
{
    [Test]
    public async Task Configure_UsesAccessTokenValidationForDefaultBearerScheme()
    {
        var options = Configure(JwtBearerDefaults.AuthenticationScheme);
        var result = await IdentityTokenOptionsTokenUseTests.ValidateTokenUseAsync(options, actualTokenUse: TokenUse.Refresh);

        await Assert.That(result.Failure).IsNotNull();
    }

    [Test]
    public async Task Configure_UsesRefreshTokenValidationForRefreshTokenScheme()
    {
        var options = Configure(IdentityAuthenticationSchemes.RefreshToken);
        var result = await IdentityTokenOptionsTokenUseTests.ValidateTokenUseAsync(options, actualTokenUse: TokenUse.Access);

        await Assert.That(result.Failure).IsNotNull();
    }

    [Test]
    public async Task Configure_SkipsNonFlammeManagedSchemes()
    {
        var configureOptions = CreateConfigureOptions();
        var options = new JwtBearerOptions();

        configureOptions.Configure("ThirdPartyBearer", options);

        await Assert.That(options.TokenValidationParameters.ValidIssuer).IsNull();
    }

    private static JwtBearerOptions Configure(string scheme)
    {
        var configureOptions = CreateConfigureOptions();
        var options = new JwtBearerOptions();

        configureOptions.Configure(scheme, options);

        return options;
    }

    private static ConfigureJwtBearerOptions CreateConfigureOptions()
    {
        return new ConfigureJwtBearerOptions(Microsoft.Extensions.Options.Options.Create(new IdentityTokenOptions
        {
            SigningKey = IdentityTokenTestConstants.SigningKey
        }));
    }
}