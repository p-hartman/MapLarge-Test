using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TestProject.Services;

namespace TestProject.Security;

// authentication to identify the token's role, endpoint policies to decide
// what it can do, and SafePathResolver to limit which objects it can reach
public static class ApiTokenDefaults
{
    public const string Scheme = "ApiToken";
    public const string ReaderRole = "Reader";
    public const string OperatorRole = "Operator";
    public const string CanReadPolicy = "CanRead";
    public const string CanWritePolicy = "CanWrite";
}

public sealed class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly FileBrowserOptions _options;

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<FileBrowserOptions> fileBrowserOptions)
        : base(schemeOptions, logger, encoder)
    {
        _options = fileBrowserOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var value = header.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Authorization header must use Bearer scheme."));
        }

        var presented = value["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(presented))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing bearer token."));
        }

        if (string.IsNullOrEmpty(_options.ReaderApiToken) && string.IsNullOrEmpty(_options.OperatorApiToken))
        {
            // reject every credential when token configuration is missing
            Logger.LogError("API tokens are not configured; rejecting all authenticated requests");
            return Task.FromResult(AuthenticateResult.Fail("Server authentication is not configured."));
        }

        string? role = null;
        if (!string.IsNullOrEmpty(_options.OperatorApiToken) &&
            ApiTokenComparer.Equals(presented, _options.OperatorApiToken))
        {
            role = ApiTokenDefaults.OperatorRole;
        }
        else if (!string.IsNullOrEmpty(_options.ReaderApiToken) &&
                 ApiTokenComparer.Equals(presented, _options.ReaderApiToken))
        {
            role = ApiTokenDefaults.ReaderRole;
        }

        if (role is null)
        {
            // never include the presented credential in logs
            Logger.LogWarning("Authentication failed for API token request from {Path}", Request.Path);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API token."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "api-client"),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, ApiTokenDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiTokenDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public static class ApiTokenAuthExtensions
{
    public static IServiceCollection AddFileBrowserAuth(this IServiceCollection services)
    {
        services.AddAuthentication(ApiTokenDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                ApiTokenDefaults.Scheme, _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ApiTokenDefaults.CanReadPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(ApiTokenDefaults.Scheme);
                policy.RequireAuthenticatedUser();
                policy.RequireRole(ApiTokenDefaults.ReaderRole, ApiTokenDefaults.OperatorRole);
            });

            options.AddPolicy(ApiTokenDefaults.CanWritePolicy, policy =>
            {
                policy.AddAuthenticationSchemes(ApiTokenDefaults.Scheme);
                policy.RequireAuthenticatedUser();
                policy.RequireRole(ApiTokenDefaults.OperatorRole);
            });
        });

        return services;
    }
}
