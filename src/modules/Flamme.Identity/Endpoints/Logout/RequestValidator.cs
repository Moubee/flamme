using FastEndpoints;
using FluentValidation;
using JetBrains.Annotations;

namespace Flamme.Identity.Endpoints.Logout;

[PublicAPI]
internal class RequestValidator : Validator<Request>
{
    public RequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}
