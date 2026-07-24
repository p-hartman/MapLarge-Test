using Microsoft.Extensions.Options;
using TestProject.Models;

namespace TestProject.Services;

// keep reads separate from mutations, can reason about their cost and
// permissions independently
public sealed class FileBrowserService
{
    private readonly SafePathResolver _paths;
    private readonly FileBrowserOptions _options;
    private readonly ILogger<FileBrowserService> _logger;

    public FileBrowserService(
        SafePathResolver paths,
        IOptions<FileBrowserOptions> options,
        ILogger<FileBrowserService> logger)
    {
        _paths = paths;
        _options = options.Value;
        _logger = logger;
    }

    public BrowseResponse Browse(string? relativePath)
    {
        var full = _paths.Resolve(relativePath);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException("Directory not found.");
        }

        var entries = new List<FileEntryDto>();

        foreach (var dir in Directory.EnumerateDirectories(full))
        {
            var info = new DirectoryInfo(dir);
            entries.Add(ToDto(info.Name, _paths.ToRelative(info.FullName), isDirectory: true, sizeBytes: null, info.LastWriteTimeUtc));
        }

        foreach (var file in Directory.EnumerateFiles(full))
        {
            var info = new FileInfo(file);
            entries.Add(ToDto(info.Name, _paths.ToRelative(info.FullName), isDirectory: false, info.Length, info.LastWriteTimeUtc));
        }

        entries = entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return BuildResponse(relativePath, entries, query: null);
    }

    public BrowseResponse Search(string? relativePath, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query is required.");
        }

        if (query.Length > 200)
        {
            throw new ArgumentException("Search query is too long.");
        }

        var root = _paths.Resolve(relativePath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("Directory not found.");
        }

        var entries = new List<FileEntryDto>();
        var remaining = _options.MaxSearchResults;

        // cap response size here, but not total traversal work. For production I
        // would also enforce an examined-entry or time budget
        foreach (var info in new DirectoryInfo(root).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            if (remaining <= 0)
            {
                break;
            }

            if (info.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                // re-check each result before exposing it as a selectable API path
                var relative = _paths.ToRelative(info.FullName);
                _ = _paths.Resolve(relative);

                long? size = info is FileInfo fi ? fi.Length : null;
                entries.Add(ToDto(info.Name, relative, info is DirectoryInfo, size, info.LastWriteTimeUtc));
                remaining--;
            }
        }

        _logger.LogInformation("Search under {Path} for query length {Len} returned {Count} hits",
            _paths.ToRelative(root), query.Length, entries.Count);

        return BuildResponse(relativePath, entries, query);
    }

    private BrowseResponse BuildResponse(string? relativePath, List<FileEntryDto> entries, string? query)
    {
        var current = string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : relativePath.Replace('\\', '/').Trim('/');

        var folderCount = entries.Count(e => e.IsDirectory);
        var fileCount = entries.Count - folderCount;
        // avoid recursive folder sizes so a browse request cannot become an unbounded walk
        var totalSize = entries.Where(e => !e.IsDirectory).Sum(e => e.SizeBytes ?? 0);

        return new BrowseResponse
        {
            CurrentPath = current,
            ParentPath = _paths.GetParentRelative(current),
            Entries = entries,
            FolderCount = folderCount,
            FileCount = fileCount,
            TotalFileSizeBytes = totalSize,
            Query = query
        };
    }

    private static FileEntryDto ToDto(
        string name,
        string relativePath,
        bool isDirectory,
        long? sizeBytes,
        DateTime lastWriteUtc)
    {
        return new FileEntryDto
        {
            Name = name,
            RelativePath = relativePath,
            IsDirectory = isDirectory,
            SizeBytes = sizeBytes,
            LastModifiedUtc = new DateTimeOffset(DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc))
        };
    }
}
