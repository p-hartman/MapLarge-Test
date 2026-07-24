using Microsoft.Extensions.Options;

namespace TestProject.Services;

// canonicalize every client path under one configured root. For this PoC I use
// lexical containment; before production I would also reject or resolve symbolic
// links and reparse points before file access
public sealed class SafePathResolver
{
    private readonly string _homeFullPath;
    private readonly ILogger<SafePathResolver> _logger;

    public SafePathResolver(IOptions<FileBrowserOptions> options, ILogger<SafePathResolver> logger)
    {
        _logger = logger;

        var configured = options.Value.HomeDirectory;
        var candidate = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(Directory.GetCurrentDirectory(), configured);

        _homeFullPath = Path.GetFullPath(candidate);

        Directory.CreateDirectory(_homeFullPath);
        _logger.LogInformation("File browser home directory locked to {Home}", _homeFullPath);
    }

    public string HomeFullPath => _homeFullPath;

    public string Resolve(string? relativePath)
    {
        var normalizedRelative = NormalizeRelative(relativePath);
        var combined = string.IsNullOrEmpty(normalizedRelative)
            ? _homeFullPath
            : Path.Combine(_homeFullPath, normalizedRelative.Replace('/', Path.DirectorySeparatorChar));

        var full = Path.GetFullPath(combined);

        if (!IsUnderHome(full))
        {
            _logger.LogWarning("Rejected path escape attempt");
            throw new UnauthorizedAccessException("Path is outside the allowed home directory.");
        }

        return full;
    }

    public string ToRelative(string fullPath)
    {
        var full = Path.GetFullPath(fullPath);
        if (!IsUnderHome(full))
        {
            throw new UnauthorizedAccessException("Path is outside the allowed home directory.");
        }

        var relative = Path.GetRelativePath(_homeFullPath, full);
        if (relative == "." || relative == string.Empty)
        {
            return string.Empty;
        }

        return relative.Replace('\\', '/');
    }

    public string? GetParentRelative(string relativePath)
    {
        var normalized = NormalizeRelative(relativePath);
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        var parent = Path.GetDirectoryName(normalized.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(parent))
        {
            return string.Empty;
        }

        return parent.Replace('\\', '/');
    }

    private bool IsUnderHome(string fullPath)
    {
        // I include the separator so "C:\home2" cannot match "C:\home".
        var homeWithSep = _homeFullPath.TrimEnd(Path.DirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;

        var target = fullPath.Equals(_homeFullPath, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return target.StartsWith(homeWithSep, StringComparison.OrdinalIgnoreCase)
               || fullPath.Equals(_homeFullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelative(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        if (relativePath.Contains('\0'))
        {
            throw new UnauthorizedAccessException("Invalid path.");
        }

        // check rooted forms before trimming so "/etc/passwd" cannot become relative
        var unified = relativePath.Replace('\\', '/');
        if (unified.StartsWith('/') || Path.IsPathRooted(relativePath))
        {
            throw new UnauthorizedAccessException("Absolute paths are not allowed.");
        }

        var trimmed = unified.Trim('/');

        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            throw new UnauthorizedAccessException("Absolute paths are not allowed.");
        }

        return trimmed;
    }
}
