using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Lumyte.Diagnostics.Server;

internal sealed class DiagnosticAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, DiagnosticServerOptions credentials)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal static bool Match(string value, string expected) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(expected));

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string authorization = Request.Headers.Authorization.ToString();
        string? role = authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            ? Match(authorization[7..], credentials.GameToken) ? "game"
                : Match(authorization[7..], credentials.OperatorToken) ? "operator" : null : null;
        if (role == null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing or invalid diagnostic credential."));
        }

        var identity = new ClaimsIdentity([new(ClaimTypes.Name, "diagnostic-" + role), new("diagnostics.role", role)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
