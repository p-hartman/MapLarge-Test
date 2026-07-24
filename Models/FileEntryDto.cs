namespace TestProject.Models;

public sealed class FileEntryDto
{
    public required string Name { get; init; }

    public required string RelativePath { get; init; }

    public required bool IsDirectory { get; init; }

    public long? SizeBytes { get; init; }

    public DateTimeOffset LastModifiedUtc { get; init; }
}
