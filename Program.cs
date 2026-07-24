using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using TestProject.Security;
using TestProject.Services;

namespace TestProject;

public class Program
{
    public static void Main(string[] args)
    {
        // create development tokens before CreateBuilder loads User Secrets
        string? devSecretsPath = null;
        var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                      ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                      ?? "Production";

        if (IsDevelopmentEnvironment(envName) && !DevApiTokenLifecycle.IsSkipped)
        {
            DevApiTokenLifecycle.ClearSessionTokens();
            devSecretsPath = DevApiTokenLifecycle.GenerateAndPersistForSession();
            Console.WriteLine();
            Console.WriteLine("Development session API tokens generated into the local key store.");
            Console.WriteLine("Token values are NOT printed (open the file to copy one into the SPA):");
            Console.WriteLine(devSecretsPath);
            Console.WriteLine("Look for plaintext hex values named:");
            Console.WriteLine($"  {DevApiTokenLifecycle.ReaderKey}");
            Console.WriteLine($"  {DevApiTokenLifecycle.OperatorKey}");
            Console.WriteLine("Do NOT open ASP.NET DataProtection-Keys XML (those are DPAPI blobs, not API tokens).");
            Console.WriteLine("Stopping the app (Ctrl+C) clears these tokens from the key store.");
            Console.WriteLine();
        }

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<FileBrowserOptions>(
            builder.Configuration.GetSection(FileBrowserOptions.SectionName));

        var homeFromEnv = Environment.GetEnvironmentVariable("FILEBROWSER_HOME");
        if (!string.IsNullOrWhiteSpace(homeFromEnv))
        {
            builder.Services.PostConfigure<FileBrowserOptions>(opts => opts.HomeDirectory = homeFromEnv);
        }

        builder.Services.AddSingleton<SafePathResolver>();
        builder.Services.AddSingleton<FileBrowserService>();
        builder.Services.AddSingleton<FileOperationService>();

        builder.Services.AddControllers();
        builder.Services.AddFileBrowserAuth();

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();

        // use one global bucket for this PoC. In a shared deployment, I would
        // partition limits by authenticated client or trusted proxy address
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("api", limiter =>
            {
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.PermitLimit = 120;
                limiter.QueueLimit = 0;
            });
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 6 * 1024 * 1024;
        });

        var app = builder.Build();

        var clearDevTokensOnExit = app.Environment.IsDevelopment() && !DevApiTokenLifecycle.IsSkipped;

        if (clearDevTokensOnExit)
        {
            DevApiTokenLifecycle.RegisterCleanupHandlers(
                app.Lifetime,
                app.Logger);
        }

        // fail closed in Prod when either secret is missing
        var fb = app.Configuration.GetSection(FileBrowserOptions.SectionName).Get<FileBrowserOptions>()
                 ?? new FileBrowserOptions();
        if (!app.Environment.IsDevelopment() &&
            (string.IsNullOrWhiteSpace(fb.ReaderApiToken) || string.IsNullOrWhiteSpace(fb.OperatorApiToken)))
        {
            throw new InvalidOperationException(
                "Production requires FileBrowser:ReaderApiToken and FileBrowser:OperatorApiToken " +
                "(prefer environment variables FileBrowser__ReaderApiToken / FileBrowser__OperatorApiToken).");
        }

        app.UseMiddleware<SecurityHeadersMiddleware>();

        // keep this early so later API failures use one safe response format
        app.UseExceptionHandler();

        app.UseHttpsRedirection();

        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapControllers();

        try
        {
            app.Run();
        }
        finally
        {
            if (clearDevTokensOnExit)
            {
                DevApiTokenLifecycle.ClearSessionTokens();
            }
        }
    }

    private static bool IsDevelopmentEnvironment(string envName) =>
        string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase);
}
