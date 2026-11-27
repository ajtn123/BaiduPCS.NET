# Architecture and API design

## Projects

| Project | Responsibility |
| --- | --- |
| `BaiduPCS.NET.Cli` | Spectre command registration, validation, prompts, shell editing and completion, and transfer and error output |
| `BaiduPCS.NET.Core` | Profile state, Baidu requests, remote paths, transfer workers, checksums, legacy file encryption, and release staging |
| `BaiduPCS.NET.Tests` | Fixture requests and responses, transfer interruption and failure tests, cryptography vectors, archive checks, and CLI parsing and localization tests |

Each supported runtime has two single-file executables, one self-contained and one framework-dependent. `BaiduPCS.NET.Core` is built into both and exposes typed APIs inside the solution.

## Command execution

The one-shot CLI and the shell pass the same argument arrays to the Spectre command app. Settings types parse options and validate local input before a service call. The shell uses the same registration data for command completion and queries the active account for remote path completion. Its history excludes commands that directly carry passwords, keys, cookies, access tokens, or sharing codes.

Commands write payloads and results to stdout. Terminal transfers and batch operations show a live table and a final results table. When redirected, commands print line output, errors go to stderr, and explicitly requested progress goes to stderr. Exit codes are `0` for success, `1` for operation or batch failure, `2` for invalid input, and `130` for interruption. `run` propagates the child process exit code. The first interrupt cancels scheduling and lets completed transfer ranges or parts be recorded. A second interrupt allows immediate process termination.

## Core API

| API | Role |
| --- | --- |
| `ProfileStore.ReadAsync` and `UpdateAsync` | Read the profile and apply an atomic, locked update to settings or account state |
| `LegacyProfile.ReadAsync` and `Merge` | Parse the Go profile without opening it for write, then merge accounts by ID and carry over Go settings |
| `PassportLogin.SignInAsync` and `IPassportPrompts` | Legacy password and verification challenge handling. Unsupported for BaiduPCS.NET sign-in. |
| `AccountIdentity.ResolveAsync` | Resolve the saved session to an account and support the configured manual-name fallback |
| `NetdiskClient` | List and search using `RemoteEntry`, read metadata and quota, perform file operations, locate links, transfer files, manage shares, query offline tasks, and manage the recycle bin |
| `FileChecksums.ComputeAsync` and `GetExportChecksumsAsync` | Compute local checksums or read remote export checksums using metadata and range requests |
| `LegacyFileCrypto` | Stream the IV-prefixed legacy AES modes and gzip format |
| `ReleaseUpdater` | Discover stable GitHub releases and verify, stage, and install the executable for the current runtime |

Construct a `NetdiskClient` with `ClientSettings` and the account whose session should be used. Dispose it after the operation or batch. Public asynchronous methods accept a `CancellationToken`. `BaiduApiException` exposes a numeric service code and HTTP status without embedding request URIs or credential values in its message.

```csharp
using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;

var store = new ProfileStore(ProfileStore.DefaultDirectory());
Profile profile = await store.ReadAsync(cancellationToken);
using var client = new NetdiskClient(profile.Settings, profile.RequireAccount());

await foreach (RemoteEntry entry in client.ListAsync("/", cancellationToken: cancellationToken))
    Console.WriteLine(entry.Path);
```

## State, transport, and transfers

Windows uses roaming application data, macOS uses `~/Library/Application Support`, and Linux uses `XDG_CONFIG_HOME` or `~/.config`. `BAIDUPCS_CONFIG_DIR` overrides the default. Settings and accounts are stored in `profile.json`. State changes use a lock and replace the file only after writing and flushing a temporary file. Unix profile files and newly created profile directories are private to the user. Windows files inherit access permissions from their enclosing directory.

The Baidu transport keeps a cookie container per client, applies endpoint-specific request formats and user agents, supports HTTP/SOCKS5 proxy routing and local interface selection, and validates HTTPS certificates. Reads retry transient connection and service failures. Operations such as permanent deletion avoid automatic retries. Passport sign-in uses an isolated session and validates redirect destinations before following them.

Downloads probe range support, write disjoint ranges to a partial file, and record completion in the destination's `.baidupcs-partial` directory. Servers that ignore ranges use a sequential transfer. Checkpoints bind ranges to the account, remote identity, size, and modification time. Uploads store session and completed part checkpoints under `uploads` in the profile directory, keyed by account and target and validated against the local file. Completed downloads replace the destination only after transfer validation. Uploads finalize with checksums in part order. Go transfer checkpoints use a different format and can be finished with BaiduPCS-Go.

## Verification

The fixture suite covers pagination, the PCS listing fallback, path matching, remote mutation errors, download signature vectors, expired links, range and part recovery, authentication challenges, archive verification, and legacy AES vectors. CI compares the one-shot CLI and shell with a synthetic profile. Authenticated comparison uses a separate profile and disposable remote files. The README records the account and service limits observed during that comparison.
