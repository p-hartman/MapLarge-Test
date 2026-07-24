# Threat model

## My scope and assumptions

I designed this as a single-tenant proof of concept. I allow a Reader to inspect the
configured home directory and an Operator to modify content inside it. I expect the
process account to have filesystem access only where the service needs it. I
download file contents as attachments and do not preview them in the application.

I generate Development tokens per run and store them temporarily in .NET User
Secrets. For Production, I expect secrets to be injected externally.

## Trust boundaries I identified

```
Browser --Bearer token + untrusted input--> ASP.NET Core --validated path--> Filesystem
```

I identified four relevant boundaries:

1. From browser to API, I treat headers, paths, JSON, multipart bodies, and filenames as untrusted.
2. From API to filesystem, I treat a relative path as a selector for a privileged OS object.
3. From configuration to process, I treat credentials and the allowed root as definitions of authority.
4. From filesystem metadata to DOM, I continue to treat persisted filenames as untrusted output.

## Risks I prioritized

### 1. Filesystem escape

I consider path traversal and absolute-path injection the highest risk because
either could expose any file reachable by the process. In `SafePathResolver`, I
reject rooted and null-containing inputs, canonicalize with `Path.GetFullPath`, and
require exact-root or root-plus-separator containment. I route every file operation
through that resolver.

I still need OS-specific testing for junctions, symlinks, and check/use races. In
Production, I would run the process under a dedicated low-privilege account with
restrictive ACLs.

### 2. Broken authorization

I map Bearer tokens to either a Reader or Operator role and gate operations with
`CanRead` and `CanWrite` policies. I keep path containment as a separate object-level
check, so even an Operator cannot intentionally select a path outside the root.

I accept that shared role tokens provide no individual identity, expiry, or per-file
permissions. For Production, I would use OIDC and subject/object authorization.

### 3. Uploaded active content

I store uploads outside `wwwroot`, block common executable and script extensions,
and serve downloads with `application/octet-stream` and attachment disposition.

I do not treat the extension denylist as content validation. For Production, I
would add an allowlist, magic-byte or parser checks, quarantine, malware scanning,
and quotas.

### 4. Resource exhaustion

I bound individual uploads at the Kestrel, action, and service layers. I cap search
result count and API request rate, and I keep browse size totals non-recursive.

I have not eliminated resource exhaustion: repeated uploads can still fill disk,
and a search with few matches can examine a large tree. For Production, I would add
total quotas, search time/depth/examined-entry budgets, bounded background work, and
distributed rate limiting.

### 5. Browser-side compromise

I keep the token in `sessionStorage` and never in the URL. I render filenames with
`textContent` and add CSP, frame denial, MIME-sniff prevention, and referrer
restrictions as defense in depth. I recognize that a successful same-origin XSS
could still read the token.

## What I left out of scope

- I did not implement multi-tenant isolation.
- I did not implement individual user lifecycle and revocation.
- I did not add content classification or malware scanning.
- I did not make recursive operations durable or transactional.
- I did not build a central audit pipeline or distributed deployment.
