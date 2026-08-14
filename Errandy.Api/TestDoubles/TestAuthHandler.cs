using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Errandy.Api.TestDoubles;

/// <summary>
/// TEMPORARY — delete once Engineer A's real JWT authentication is wired in.
/// Auto-authenticates every request locally as a fake user who holds ALL
/// roles (Customer, Runner, Admin) simultaneously, purely so you can test
/// every [Authorize(Roles = "...")] endpoint through Swagger without a real
/// login flow existing yet. NEVER ship this — it bypasses auth entirely.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim(ClaimTypes.Role, "Runner"),
            new Claim(ClaimTypes.Role, "Admin")
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}