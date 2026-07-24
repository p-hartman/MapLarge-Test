namespace TestProject.Models;

public sealed class BrowseResponse
{
    public required string CurrentPath { get; init; }

    public string? ParentPath { get; init; }

    public required IReadOnlyList<FileEntryDto> Entries { get; init; }

    public int FolderCount { get; init; }

    public int FileCount { get; init; }

    public long TotalFileSizeBytes { get; init; }

    public string? Query { get; init; }
}
