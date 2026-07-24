namespace TestProject.Services;

public sealed class FileBrowserOptions
{
    public const string SectionName = "FileBrowser";

    // resolve relative paths from the application content root
    public string HomeDirectory { get; set; } = "SandboxHome";

    // leave these empty in base configuration and inject Production values externally
    public string ReaderApiToken { get; set; } = string.Empty;

    public string OperatorApiToken { get; set; } = string.Empty;

    public long MaxUploadBytes { get; set; } = 5 * 1024 * 1024;

    // use this as demo hardening, not content validation; would prefer an allowlist in Production
    public string[] BlockedUploadExtensions { get; set; } =
    [
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".sh", ".msi", ".com", ".scr", ".js", ".vbs", ".wsf"
    ];

    public int MaxSearchResults { get; set; } = 200;
}
