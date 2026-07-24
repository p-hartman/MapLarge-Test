namespace TestProject.Models;

public sealed class LoginRequest
{
    public required string ApiToken { get; init; }
}

public sealed class LoginResponse
{
    public required string Role { get; init; }
    public required string Message { get; init; }
}

public sealed class PathMutationRequest
{
    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }
}

public sealed class DeleteRequest
{
    public required string Path { get; init; }
}
