using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace TestProject.Security;

// domain failures bubble out of controllers so I can map them consistently here
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, message) = Map(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            // stack trace in server logs and return a stable, non-leaking message
            _logger.LogError(exception, "Unhandled API exception");
            message = "Unexpected error.";
        }
        else
        {
            _logger.LogDebug(exception, "Handled API exception as {StatusCode}", statusCode);
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        var body = new Dictionary<string, object?>
        {
            ["type"] = $"https://httpstatuses.com/{statusCode}",
            ["title"] = ReasonTitle(statusCode),
            ["status"] = statusCode,
            ["detail"] = message,
            ["error"] = message, // retained for the small SPA client
            ["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier
        };

        await httpContext.Response.WriteAsJsonAsync(body, cancellationToken);
        return true;
    }

    private static (int StatusCode, string Message) Map(Exception exception) =>
        exception switch
        {
            UnauthorizedAccessException ex =>
                (StatusCodes.Status403Forbidden, SafeMessage(ex, "Forbidden.")),

            FileNotFoundException ex =>
                (StatusCodes.Status404NotFound, SafeMessage(ex, "Not found.")),

            DirectoryNotFoundException ex =>
                (StatusCodes.Status404NotFound, SafeMessage(ex, "Not found.")),

            ArgumentException ex =>
                (StatusCodes.Status400BadRequest, SafeMessage(ex, "Bad request.")),

            InvalidOperationException ex =>
                (StatusCodes.Status409Conflict, SafeMessage(ex, "Conflict.")),

            _ => (StatusCodes.Status500InternalServerError, "Unexpected error.")
        };

    private static string SafeMessage(Exception ex, string fallback) =>
        string.IsNullOrWhiteSpace(ex.Message) ? fallback : ex.Message;

    private static string ReasonTitle(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status500InternalServerError => "Internal Server Error",
        _ => "Error"
    };
}
