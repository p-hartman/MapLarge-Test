using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TestProject.Services;

namespace TestProject.Tests;

public class SafePathResolverTests : IDisposable
{
    private readonly string _home;
    private readonly SafePathResolver _resolver;

    public SafePathResolverTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "fb-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(Path.Combine(_home, "docs"));

        var options = Options.Create(new FileBrowserOptions { HomeDirectory = _home });
        _resolver = new SafePathResolver(options, NullLogger<SafePathResolver>.Instance);
    }

    [Fact]
    public void Resolve_EmptyPath_ReturnsHome()
    {
        var full = _resolver.Resolve("");
        Assert.Equal(Path.GetFullPath(_home), full);
    }

    [Fact]
    public void Resolve_ChildFolder_Succeeds()
    {
        var full = _resolver.Resolve("docs");
        Assert.Equal(Path.GetFullPath(Path.Combine(_home, "docs")), full);
    }

    [Theory]
    [InlineData("../")]
    [InlineData("..\\")]
    [InlineData("docs/../../")]
    [InlineData("docs\\..\\..\\Windows")]
    public void Resolve_Traversal_ThrowsUnauthorized(string evil)
    {
        Assert.Throws<UnauthorizedAccessException>(() => _resolver.Resolve(evil));
    }

    [Theory]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("/etc/passwd")]
    public void Resolve_AbsolutePath_ThrowsUnauthorized(string evil)
    {
        Assert.Throws<UnauthorizedAccessException>(() => _resolver.Resolve(evil));
    }

    [Fact]
    public void Resolve_NullByte_ThrowsUnauthorized()
    {
        Assert.Throws<UnauthorizedAccessException>(() => _resolver.Resolve("docs\0.txt"));
    }

    [Fact]
    public void PrefixBypass_HomeSibling_IsRejected()
    {
        var sibling = _home.TrimEnd(Path.DirectorySeparatorChar) + "2";
        Directory.CreateDirectory(sibling);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                _resolver.Resolve(".." + Path.DirectorySeparatorChar + Path.GetFileName(sibling));
            });
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public void SanitizeFileName_StripsDirectories()
    {
        var clean = FileOperationService.SanitizeFileName("..\\..\\evil.txt");
        Assert.Equal("evil.txt", clean);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }
}
