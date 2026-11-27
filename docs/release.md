# Release

The CI workflow restores locked dependencies, runs the offline tests, and compares one-shot and shell behavior with a synthetic profile on Linux, macOS, and Windows. It installs the packed .NET tool from a local feed and builds and runs all eight standalone archives on matching GitHub-hosted runners. The musl checks run the extracted executables inside the .NET Alpine runtime-dependencies image. [Hosted runner labels](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)

## Publishing setup

Create a NuGet.org Trusted Publishing policy for repository owner `ajtn123`, repository `BaiduPCS.NET`, workflow `ci.yml`, and environment `release`. Enable publishing a new package for the first release and new versions for later releases. Scope the package pattern to `BaiduPCS.NET`. Set the `NUGET_USER` secret in the GitHub `release` environment to the NuGet.org account's profile name. The workflow exchanges its identity token for a temporary publishing key. [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)

## First release

1. Complete the user-assisted BaiduPCS-Go comparison. Record service limitations and resolve client regressions.
2. Confirm the latest main workflow passed on the reviewed commit. Push a `v1.0.0` tag on that commit. The workflow will repack the binaries with the tag version and rerun the tests.
3. Confirm the NuGet tool and GitHub release are publicly available. Verify the eight standalone archive hashes against `SHA256SUMS` and check that `baidupcs update check` discovers the release.
