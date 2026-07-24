using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TestProject.Models;
using TestProject.Security;
using TestProject.Services;

namespace TestProject.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly FileBrowserOptions _options;

    public AuthController(IOptions<FileBrowserOptions> options)
    {
        _options = options.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ApiToken))
        {
            return BadRequest(new { error = "API token is required." });
        }

        if (!string.IsNullOrEmpty(_options.OperatorApiToken) &&
            ApiTokenComparer.Equals(request.ApiToken, _options.OperatorApiToken))
        {
            return Ok(new LoginResponse
            {
                Role = ApiTokenDefaults.OperatorRole,
                Message = "Authenticated as Operator (read + write)."
            });
        }

        if (!string.IsNullOrEmpty(_options.ReaderApiToken) &&
            ApiTokenComparer.Equals(request.ApiToken, _options.ReaderApiToken))
        {
            return Ok(new LoginResponse
            {
                Role = ApiTokenDefaults.ReaderRole,
                Message = "Authenticated as Reader (read-only)."
            });
        }

        return Unauthorized(new { error = "Invalid API token." });
    }

    [HttpGet("me")]
    [Authorize(Policy = ApiTokenDefaults.CanReadPolicy)]
    public ActionResult<object> Me()
    {
        var role = User.IsInRole(ApiTokenDefaults.OperatorRole)
            ? ApiTokenDefaults.OperatorRole
            : ApiTokenDefaults.ReaderRole;

        return Ok(new { role });
    }
}
