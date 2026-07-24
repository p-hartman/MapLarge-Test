# My Dependency Inventory

I intentionally minimized third-party packages. For this security-engineering
submission, I wanted to explain what every dependency does, why I included it, and
how it could fail or expand the attack surface.

## Runtime and platform dependencies I use

### .NET 8 / ASP.NET Core (`Microsoft.NET.Sdk.Web`)

| Item | Detail |
|------|--------|
| **What I use** | I use the application framework for Kestrel, MVC controllers, static files, authentication, and rate limiting. |
| **Why I use it** | I needed a C# web service for the exercise and chose its battle-tested HTTP primitives. |
| **Risks I considered** | I considered framework CVEs such as request smuggling or Kestrel bugs, incorrect middleware order, and unexpected default JSON behavior. |
| **How I mitigate them** | I pin the SDK/runtime to patched versions, keep middleware order explicit, fail closed without Production tokens, and limit request body size. |
| **How I track its supply chain** | I consume Microsoft's shared framework and would monitor [MSRC](https://msrc.microsoft.com/) advisories. |

### `System.IO` / BCL file APIs

| Item | Detail |
|------|--------|
| **What I use** | I use the built-in `File`, `Directory`, `Path`, and `FileStream` APIs. |
| **Why I use it** | I need filesystem access for browse, upload, and download, but I do not need an ORM or cloud SDK. |
| **Risks I considered** | I considered path traversal if code bypasses `SafePathResolver`, symlink escapes, TOCTOU races, and disk exhaustion. |
| **How I mitigate them** | I canonicalize and prefix-check paths with a trailing separator, limit uploads, block selected extensions, publish uploads through temporary files, and reject user-controlled absolute paths. |

### `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals`

| Item | Detail |
|------|--------|
| **What I use** | I use a constant-time byte comparison. |
| **Why I use it** | I compare API tokens without introducing a trivial timing oracle. |
| **Risks I considered** | I do not treat it as password hashing or salting, and it cannot help if I leak tokens elsewhere. |
| **How I mitigate them** | I keep tokens in environment configuration or a secret manager and never log Authorization headers. |

### ASP.NET Core Rate Limiting (`Microsoft.AspNetCore.RateLimiting`)

| Item | Detail |
|------|--------|
| **What I use** | I apply the built-in fixed-window limiter to `/api/files/*`. |
| **Why I use it** | I wanted a coarse DoS and token-brute-force brake without another NuGet package. |
| **Risks I considered** | I know in-memory counters reset per process, become uneven across instances, and can reject legitimate bursts with 429. |
| **How I would extend it** | For a multi-node Production system, I would use a distributed limiter or edge WAF and tune limits to the threat model. |

### Browser platform (no npm packages)

| Item | Detail |
|------|--------|
| **What I use** | I use vanilla ES modules with `fetch`, `sessionStorage`, `<dialog>`, and Hash URL APIs. |
| **Why I use it** | I followed the exercise's framework restriction and avoided a frontend package supply chain. |
| **Risks I considered** | I know a DOM XSS could steal the token from `sessionStorage`, and older browsers may not support `<dialog>`. |
| **How I mitigate them** | I render entry names with `textContent`, enforce CSP `script-src 'self'`, and never place tokens in URLs. |

## Development and test dependencies I use

I declare these under `TestProject.Tests`:

| Package | Why I use it | Risks I considered |
|---------|--------------|--------------------|
| `Microsoft.NET.Test.Sdk` | I use it as the test host. | I keep this standard component updated. |
| `xunit` / `xunit.runner.visualstudio` | I use xUnit as the test framework and the runner for IDE/test-host integration. | I keep both standard components updated. |
| `Microsoft.AspNetCore.Mvc.Testing` | I use an in-memory `WebApplicationFactory` for HTTP security tests. | I boot the real pipeline, so I isolate tests from Production and development secrets. |
| `Microsoft.Extensions.Options` | I use the shared-framework options types in service tests. | I add no separate runtime package for it. |

### Local token generator / session lifecycle (`DevApiTokenLifecycle` + optional `tools/GenerateApiTokens`)

| Item | Detail |
|------|--------|
| **What I built** | I built a Development session manager that writes Reader/Operator tokens into **.NET User Secrets** on `dotnet run` and clears them on exit. |
| **Why I built it** | I wanted simple local onboarding without printing secrets and fewer leftover credentials after a demo. |
| **How I store tokens** | I write directly to `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`. |
| **How I clean up** | I remove FileBrowser keys on `ApplicationStopping`, `Ctrl+C`, `ProcessExit`, and in a `finally` block. |
| **Risks I considered** | I cannot clean up after every forced kill; I also account for confusion between DataProtection XML and `secrets.json`, and I make tests set `FILEBROWSER_SKIP_DEV_TOKEN_LIFECYCLE=1`. |
| **How I handle Production** | I do not use this lifecycle in Production; I expect environment injection or a vault. |

I added **no** authentication library (Identity or JWT bearer), ORM, client SPA
bundler, or custom cryptography beyond `FixedTimeEquals` and
`RandomNumberGenerator`.

## Configuration and secret dependencies I use

| Source | How I use it | Failure modes I considered |
|--------|--------------|----------------------------|
| `appsettings.json` | I store non-secret defaults such as the relative home path, size limits, and AllowedHosts. | I leave tokens empty so I cannot leak them through git. |
| .NET User Secrets | I store per-run Development Reader/Operator tokens. | I treat this as a local developer store, not an encrypted Production vault, and remove generated values on clean shutdown. |
| Environment variables `FileBrowser__ReaderApiToken`, `FileBrowser__OperatorApiToken`, `FILEBROWSER_HOME` | I inject Production secrets and configuration through these variables. | I account for visibility to other administrators and shell history, and I refuse Production startup when variables are missing. |

## Implicit operational dependencies I considered

| Dependency | Why I need it | Failures I considered |
|------------|---------------|-----------------------|
| NTFS / local disk hosting `SandboxHome` | I use local disk as the PoC data plane. | I expect overly broad permissions to expose the sandbox to local users, and a full disk to break uploads. |
| TLS certificate (development certificate or reverse proxy) | I need TLS to protect Bearer tokens in transit. | I treat cleartext HTTP on a real network as credential exposure. |
| Host OS account running the web process | I inherit this account's privileges for every file operation. | I would not run as Administrator/root because a path bug could then become a host compromise. |

## Dependencies I deliberately excluded

| What I excluded | Why I excluded it |
|-----------------|-------------------|
| React / Angular / Vue | I followed the exercise requirement and also reduced framework and frontend supply-chain surface. |
| JWT/OIDC libraries | I avoided key-management complexity in this PoC and used explainable shared role secrets. |
| Swagger/Swashbuckle | I avoided extra surface and noise for a small API that did not require it. |
| Third-party file managers / OSS browser widgets | I wanted reviewers to see my file-browser implementation directly. |
| Custom cryptography / homegrown TLS | I consider both out of scope and unsafe. |

## How I evaluate dependency changes

1. If I add a NuGet package with a native binary, I add potential supply-chain malware exposure.
2. If I add `UseCors(AllowAnyOrigin)`, I could enable cross-site API abuse with a stolen token.
3. If I add cookie authentication without antiforgery, I could introduce CSRF on delete or upload.
4. If I add rich HTML file previews, I could introduce stored XSS from an untrusted upload.

For any future dependency, I would update this document with its purpose, trust
boundary impact, failure modes, and update owner.
