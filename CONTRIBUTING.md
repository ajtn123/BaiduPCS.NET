# Contributing to BaiduPCS.NET

Use an installed .NET SDK that can target `net10.0`. Run the CLI and offline tests from the repository root.

```sh
dotnet restore BaiduPCS.NET.slnx --locked-mode
dotnet build BaiduPCS.NET.slnx --no-restore
dotnet test --solution BaiduPCS.NET.slnx --no-build --no-restore
dotnet run --project src/BaiduPCS.NET.Cli -- --help
```

## Report a bug

Check existing issues first. Include the BaiduPCS.NET version, operating system and architecture, command, expected behavior, and sanitized output. For Baidu service errors, include the numeric error code and whether the same operation works in BaiduPCS-Go or the official client, if you have checked. [Issues](https://github.com/ajtn123/BaiduPCS.NET/issues)

Keep profile files, cookies, tokens, share links, and private paths out of public reports. For a suspected vulnerability, use the private reporting channel. [Security policy](SECURITY.md)

## Submit a change

- Explain the behavior change and how you verified it in the pull request.
- Keep command parsing and display in the CLI project. Keep Netdisk requests and transfer logic in the core project. [Architecture](docs/architecture.md)
- Use synthetic responses for protocol tests. For live testing, use a separate `BAIDUPCS_CONFIG_DIR` and disposable remote files.
- Update the command reference when adding a command, option, or alias. Update the dependency lock file when changing package references. [Command reference](docs/commands.md)
