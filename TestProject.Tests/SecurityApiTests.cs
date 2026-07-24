using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TestProject.Security;

namespace TestProject.Tests;

public class SecurityApiTests : IClassFixture<FileBrowserWebApplicationFactory>
{
    private readonly FileBrowserWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private const string ReaderToken = "test-reader-token";
    private const string OperatorToken = "test-operator-token";

    public SecurityApiTests(FileBrowserWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Browse_WithoutToken_IsUnauthorized()
    {
        var response = await _client.GetAsync("/api/files/browse");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Browse_WithInvalidToken_IsUnauthorized()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/files/browse");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Browse_WithReaderToken_Succeeds()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/files/browse");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReaderToken);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("folderCount", out _));
        Assert.True(json.TryGetProperty("fileCount", out _));
        Assert.True(json.TryGetProperty("totalFileSizeBytes", out _));
    }

    [Fact]
    public async Task Browse_TraversalPath_IsForbidden()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/files/browse?path=..%2F..%2FWindows");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OperatorToken);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithReaderToken_IsForbidden()
    {
        var relative = "notes-authz.txt";
        await File.WriteAllTextAsync(Path.Combine(_factory.HomeDirectory, relative), "x");

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/files/delete")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { path = relative }),
                Encoding.UTF8,
                "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReaderToken);

        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // verify both the denial and the authorization ran before the side effect
        Assert.True(File.Exists(Path.Combine(_factory.HomeDirectory, relative)));
    }

    [Fact]
    public async Task Delete_WithOperatorToken_Succeeds()
    {
        var relative = "notes-operator.txt";
        await File.WriteAllTextAsync(Path.Combine(_factory.HomeDirectory, relative), "x");

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/files/delete")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { path = relative }),
                Encoding.UTF8,
                "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OperatorToken);

        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(_factory.HomeDirectory, relative)));
    }

    [Fact]
    public async Task Upload_BlockedExtension_IsRejected()
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("echo hi"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", "payload.ps1");

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/files/upload")
        {
            Content = content
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OperatorToken);

        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Search_FindsSandboxFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_factory.HomeDirectory, "invoice-findme.txt"), "demo");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/files/search?q=invoice-findme");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReaderToken);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("fileCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task SecurityHeaders_ArePresent_OnIndex()
    {
        var response = await _client.GetAsync("/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }
}

public sealed class FileBrowserWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    // give the test class an isolated root instead of using my SandboxHome.
    public string HomeDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "fb-api-tests-" + Guid.NewGuid().ToString("N"));

    public FileBrowserWebApplicationFactory()
    {
        // inject fixed test credentials without rotating my development User Secrets
        Environment.SetEnvironmentVariable(DevApiTokenLifecycle.SkipEnvironmentVariable, "1");

        Directory.CreateDirectory(HomeDirectory);
        Directory.CreateDirectory(Path.Combine(HomeDirectory, "docs"));
        File.WriteAllText(Path.Combine(HomeDirectory, "readme.txt"), "test home");
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("FileBrowser:HomeDirectory", HomeDirectory);
        builder.UseSetting("FileBrowser:ReaderApiToken", "test-reader-token");
        builder.UseSetting("FileBrowser:OperatorApiToken", "test-operator-token");
        builder.UseSetting(WebHostDefaults.EnvironmentKey, "Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileBrowser:HomeDirectory"] = HomeDirectory,
                ["FileBrowser:ReaderApiToken"] = "test-reader-token",
                ["FileBrowser:OperatorApiToken"] = "test-operator-token"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(HomeDirectory))
        {
            try { Directory.Delete(HomeDirectory, recursive: true); } catch { /* best effort */ }
        }
    }
}
