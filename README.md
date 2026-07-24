# Secure File Browser

I built this ASP.NET Core 8 API and vanilla JavaScript SPA to browse a configured
server directory. I support browsing, name search, upload, download, copy, move,
and delete, and I deep-link the UI state in the URL hash.

I treated this as a proof of concept. I focused on the filesystem trust boundary
and kept the runtime dependency surface small.

## How I run it locally

I use the .NET 8 SDK and a current browser.

```powershell
dotnet restore
dotnet run
```

On each Development run, I create fresh Reader and Operator tokens in .NET User
Secrets. I print the path to `secrets.json`, but never the token values. I open that
file and paste one token into the sign-in form:

- Reader: browse, search, download
- Operator: Reader access plus upload, copy, move, delete

When I press `Ctrl+C`, I remove the session tokens. I cannot guarantee cleanup after
a forced process kill, so I replace any leftover values on the next run.

I use `SandboxHome` by default and override it with `FILEBROWSER_HOME` when needed.

## How I test it

```powershell
dotnet test
```

I cover path traversal, absolute paths, prefix bypasses, missing or invalid
credentials, Reader/Operator authorization, blocked uploads, search, and response
security headers.

## My design

```
Browser SPA
    |
    | Bearer token + relative path
    v
Controllers (HTTP and policies)
    |
    v
FileBrowserService / FileOperationService
    |
    v
SafePathResolver
    |
    v
Configured home directory
```

I use `SafePathResolver` to canonicalize every caller-provided path. I require the
result to equal the configured root or begin with the root plus a directory
separator. I use that check to keep an authenticated Operator inside the same
filesystem boundary as a Reader.

I keep authentication and authorization separate:

- I map a valid token to a role in the authentication handler;
- I permit read or write operations through endpoint policies;
- I decide whether the target is inside the sandbox through the path resolver.

I serve the SPA and API from the same origin, so I do not enable CORS. I render
filenames with `textContent`. I return downloads as attachments with
`application/octet-stream` instead of rendering them in the application origin.

## How I store URL state

I use links such as:

```text
#/browse?path=docs&q=invoice
```

I put the path and search term in the URL, but never credentials.

## My configuration

I keep non-secret defaults in `appsettings.json`. For Production, I inject
credentials through a secret manager or environment:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:FileBrowser__ReaderApiToken = "<reader-secret>"
$env:FileBrowser__OperatorApiToken = "<operator-secret>"
$env:FILEBROWSER_HOME = "D:\Data\Sandbox"
dotnet run --no-launch-profile
```

I fail Production startup if either token is absent.

## Limitations I accepted

- I use shared role tokens rather than full OIDC identities.
- I block upload extensions with a denylist; for Production I would use an allowlist
  plus content and malware inspection.
- I cap search results, but I do not yet cap total examined entries.
- I do not make recursive copy transactional, so a failure may leave partial output.
- I keep rate-limit state in memory and therefore per process.
- I have not completed OS-specific hardening for junctions, symlinks, and TOCTOU races.
- I intentionally keep folder sizes non-recursive to bound browse latency.

Before internet exposure I would add OIDC, per-object authorization, a low-privilege
service account and strict filesystem ACLs, upload quarantine/scanning, quotas,
central audit logging, distributed limits, health checks, and indexed/paginated
search.

I recorded my security assumptions in `docs/THREAT_MODEL.md` and my dependency
inventory in `docs/DEPENDENCIES.md`.
