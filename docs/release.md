# Release

The CI workflow restores locked dependencies, runs the offline tests, and compares one-shot and shell behavior with a synthetic profile on Linux, macOS, and Windows. It builds and runs both single-file variants for each of eight runtime identifiers on matching GitHub-hosted runners. The musl checks use the .NET Alpine runtime image for framework-dependent builds and the runtime-dependencies image for self-contained builds. [Hosted runner labels](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)

## Publishing

1. Complete the user-assisted BaiduPCS-Go comparison. Record service limitations and resolve client regressions.
2. Confirm CI passed on the reviewed commit. Push a stable version tag such as `v1.1.0` on that commit. The workflow rebuilds and smoke-tests the tagged executables before publishing the GitHub release.
3. Verify the sixteen executables against `SHA256SUMS`, confirm the combined license and runtime notices are in `LICENSES.txt`, and check that `baidupcs update check` discovers the release.

Self-contained assets use `BaiduPCS.NET-v<VERSION>-<runtime>`. Framework-dependent assets add `-framework-dependent` to that name. Windows executables end in `.exe`. The framework-dependent executable prefers .NET 10 and uses the next installed major runtime when .NET 10 is unavailable. The updater preserves the installed variant. [Runtime version selection](https://learn.microsoft.com/en-us/dotnet/core/versions/selection)
