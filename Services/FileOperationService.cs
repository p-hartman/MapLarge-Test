using Microsoft.Extensions.Options;

namespace TestProject.Services;

public sealed class FileOperationService
{
    private readonly SafePathResolver _paths;
    private readonly FileBrowserOptions _options;
    private readonly ILogger<FileOperationService> _logger;

    public FileOperationService(
        SafePathResolver paths,
        IOptions<FileBrowserOptions> options,
        ILogger<FileOperationService> logger)
    {
        _paths = paths;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SaveUploadAsync(string? destinationDirectory, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
        {
            throw new ArgumentException("Upload is empty.");
        }

        if (file.Length > _options.MaxUploadBytes)
        {
            throw new InvalidOperationException($"Upload exceeds maximum size of {_options.MaxUploadBytes} bytes.");
        }

        var safeName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(safeName);

        // treat extension blocking as defense in depth and store files outside wwwroot
        if (_options.BlockedUploadExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Uploads with extension '{extension}' are blocked.");
        }

        var destDir = _paths.Resolve(destinationDirectory);
        Directory.CreateDirectory(destDir);

        var destFull = _paths.Resolve(CombineRelative(destinationDirectory, safeName));
        // do not silently overwrite an existing file or directory
        if (File.Exists(destFull) || Directory.Exists(destFull))
        {
            throw new InvalidOperationException("A file or folder with that name already exists.");
        }

        // publish only completed uploads and make cleanup best-effort on failure
        var tempFull = destFull + ".uploading";
        try
        {
            await using (var stream = new FileStream(tempFull, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream, ct);
            }

            File.Move(tempFull, destFull);
            _logger.LogInformation("Uploaded file to {Relative}", _paths.ToRelative(destFull));
        }
        catch
        {
            if (File.Exists(tempFull))
            {
                File.Delete(tempFull);
            }

            throw;
        }
    }

    public Stream OpenDownload(string relativePath, out string downloadFileName, out long length)
    {
        var full = _paths.Resolve(relativePath);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException("File not found.");
        }

        var info = new FileInfo(full);
        downloadFileName = info.Name;
        length = info.Length;

        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public void Delete(string relativePath)
    {
        var full = EnsureNotHome(relativePath);

        if (Directory.Exists(full))
        {
            Directory.Delete(full, recursive: true);
            _logger.LogInformation("Deleted directory {Relative}", _paths.ToRelative(full));
            return;
        }

        if (File.Exists(full))
        {
            File.Delete(full);
            _logger.LogInformation("Deleted file {Relative}", _paths.ToRelative(full));
            return;
        }

        throw new FileNotFoundException("Path not found.");
    }

    public void Copy(string sourceRelative, string destinationRelative)
    {
        var source = EnsureNotHome(sourceRelative);
        var dest = _paths.Resolve(destinationRelative);

        if (File.Exists(dest) || Directory.Exists(dest))
        {
            throw new InvalidOperationException("Destination already exists.");
        }

        var destParent = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destParent))
        {
            _ = _paths.Resolve(_paths.ToRelative(destParent));
            Directory.CreateDirectory(destParent);
        }

        if (File.Exists(source))
        {
            File.Copy(source, dest);
            return;
        }

        if (Directory.Exists(source))
        {
            CopyDirectory(source, dest);
            return;
        }

        throw new FileNotFoundException("Source not found.");
    }

    public void Move(string sourceRelative, string destinationRelative)
    {
        var source = EnsureNotHome(sourceRelative);
        var dest = _paths.Resolve(destinationRelative);

        if (File.Exists(dest) || Directory.Exists(dest))
        {
            throw new InvalidOperationException("Destination already exists.");
        }

        var destParent = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destParent))
        {
            _ = _paths.Resolve(_paths.ToRelative(destParent));
            Directory.CreateDirectory(destParent);
        }

        if (File.Exists(source))
        {
            File.Move(source, dest);
            return;
        }

        if (Directory.Exists(source))
        {
            Directory.Move(source, dest);
            return;
        }

        throw new FileNotFoundException("Source not found.");
    }

    private string EnsureNotHome(string relativePath)
    {
        var full = _paths.Resolve(relativePath);
        if (full.Equals(_paths.HomeFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The home directory itself cannot be modified this way.");
        }

        return full;
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        // keep recursive copy simple for the PoC; it is not transactional and can
        // leave partial output if an operation fails
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            var target = Path.Combine(destDir, name);
            _ = _paths.Resolve(_paths.ToRelative(target));
            File.Copy(file, target);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(dir);
            var target = Path.Combine(destDir, name);
            _ = _paths.Resolve(_paths.ToRelative(target));
            CopyDirectory(dir, target);
        }
    }

    private static string CombineRelative(string? directory, string fileName)
    {
        var dir = string.IsNullOrWhiteSpace(directory)
            ? string.Empty
            : directory.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(dir) ? fileName : $"{dir}/{fileName}";
    }

    internal static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Invalid file name.");
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        if (IsWindowsReservedDeviceName(stem))
        {
            name = "_" + name;
        }

        return name;
    }

    private static bool IsWindowsReservedDeviceName(string stem)
    {
        string[] reserved =
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ];
        return reserved.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }
}
