using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TestProject.Models;
using TestProject.Security;
using TestProject.Services;

namespace TestProject.Controllers;

[ApiController]
[Route("api/files")]
[EnableRateLimiting("api")]
public sealed class FilesController : ControllerBase
{
    private readonly FileBrowserService _browser;
    private readonly FileOperationService _operations;

    public FilesController(FileBrowserService browser, FileOperationService operations)
    {
        _browser = browser;
        _operations = operations;
    }

    [HttpGet("browse")]
    [Authorize(Policy = ApiTokenDefaults.CanReadPolicy)]
    public ActionResult<BrowseResponse> Browse([FromQuery] string? path) =>
        Ok(_browser.Browse(path));

    [HttpGet("search")]
    [Authorize(Policy = ApiTokenDefaults.CanReadPolicy)]
    public ActionResult<BrowseResponse> Search([FromQuery] string? path, [FromQuery] string q) =>
        Ok(_browser.Search(path, q));

    [HttpGet("download")]
    [Authorize(Policy = ApiTokenDefaults.CanReadPolicy)]
    public IActionResult Download([FromQuery] string path)
    {
        var stream = _operations.OpenDownload(path, out var fileName, out _);

        // I force downloads so user-controlled HTML or SVG cannot render in this origin.
        return File(stream, "application/octet-stream", fileName);
    }

    [HttpPost("upload")]
    [Authorize(Policy = ApiTokenDefaults.CanWritePolicy)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromQuery] string? path, IFormFile file, CancellationToken ct)
    {
        await _operations.SaveUploadAsync(path, file, ct);
        return Ok(new { message = "Upload succeeded." });
    }

    [HttpPost("delete")]
    [Authorize(Policy = ApiTokenDefaults.CanWritePolicy)]
    public IActionResult Delete([FromBody] DeleteRequest request)
    {
        _operations.Delete(request.Path);
        return Ok(new { message = "Deleted." });
    }

    [HttpPost("copy")]
    [Authorize(Policy = ApiTokenDefaults.CanWritePolicy)]
    public IActionResult Copy([FromBody] PathMutationRequest request)
    {
        _operations.Copy(request.SourcePath, request.DestinationPath);
        return Ok(new { message = "Copied." });
    }

    [HttpPost("move")]
    [Authorize(Policy = ApiTokenDefaults.CanWritePolicy)]
    public IActionResult Move([FromBody] PathMutationRequest request)
    {
        _operations.Move(request.SourcePath, request.DestinationPath);
        return Ok(new { message = "Moved." });
    }
}
