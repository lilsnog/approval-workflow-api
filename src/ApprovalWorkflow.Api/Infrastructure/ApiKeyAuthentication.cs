using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using ApprovalWorkflow.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ApprovalWorkflow.Infrastructure;

public sealed class ApiKeyUser
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
}

public sealed class ApiKeyOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "ApiKey";
    public const string Header = "X-Api-Key";

    public List<ApiKeyUser> Users { get; set; } = new();
}

/// <summary>
/// Simple API-key authentication for the demo: each key maps to a user and role.
/// In production, put this API behind your identity provider (JWT / OpenID Connect)
/// and read the name and role claims from the token instead. Nothing else changes.
/// </summary>
public sealed class ApiKeyHandler(
    IOptionsMonitor<ApiKeyOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyOptions.Header, out var supplied) || string.IsNullOrEmpty(supplied))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var suppliedBytes = Encoding.UTF8.GetBytes(supplied.ToString());

        // Constant-time comparison so keys can't be guessed byte-by-byte from timing.
        var user = Options.Users.FirstOrDefault(u =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(u.Key), suppliedBytes));

        if (user is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Role, user.Role)],
            ApiKeyOptions.Scheme);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyOptions.Scheme)));
    }
}

public static class ClaimsPrincipalExtensions
{
    public static CurrentUser ToCurrentUser(this ClaimsPrincipal principal) =>
        new(
            principal.Identity?.Name ?? throw new InvalidOperationException("Unauthenticated."),
            principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty);
}
