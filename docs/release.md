# Release

The CI workflow restores locked dependencies, runs the offline tests, and compares one-shot and shell behavior with a synthetic profile on Linux, macOS, and Windows. It builds and runs the eight self-contained executables on matching GitHub-hosted runners. The musl checks run inside the .NET Alpine runtime-dependencies image. [Hosted runner labels](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)

## Publishing

1. Complete the user-assisted BaiduPCS-Go comparison. Record service limitations and resolve client regressions.
2. Confirm CI passed on the reviewed commit. Push a stable version tag such as `v1.0.0` on that commit. The workflow rebuilds and smoke-tests the tagged executables before publishing the GitHub release.
3. Verify the eight executables against `SHA256SUMS`, confirm the combined license and runtime notices are in `LICENSES.txt`, and check that `baidupcs update check` discovers the release.

Release assets follow `BaiduPCS.NET-v<VERSION>-<runtime>`. Windows executables end in `.exe`.
