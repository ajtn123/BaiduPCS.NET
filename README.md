# BaiduPCS.NET

BaiduPCS.NET is a .NET 10 command-line client for Baidu Netdisk. It ports the workflows of [BaiduPCS-Go](https://github.com/qjfoidnh/BaiduPCS-Go) to Spectre.Console.Cli and provides an interactive shell and commands for scripts.

## Quick start

Install the published .NET tool with the .NET 10 SDK.

```sh
dotnet tool install --global BaiduPCS.NET
baidupcs --help
```

To run from source, build or run the CLI project with the .NET 10 SDK.

```sh
dotnet run --project src/BaiduPCS.NET.Cli -- --help
```

Import an existing BaiduPCS-Go profile, or sign in interactively.

```sh
baidupcs account import-go
baidupcs account current
baidupcs quota
baidupcs list /
```

`account import-go` reads the existing profile and writes a separate BaiduPCS.NET profile. Pass the path to `pcs_config.json` for a portable installation. `account login` prompts for the username and password. To pass existing cookies from a credential source, use `account login --cookies-stdin`. BDUSS sign-in also accepts optional PTOKEN and STOKEN.

```sh
baidupcs mkdir /example
baidupcs upload ./notes.txt /example
baidupcs download /example/notes.txt --output-directory ./downloads
baidupcs shell
```

Run `baidupcs` in a terminal to enter the shell. Use `help`, Tab completion, and `exit`. The active account's remote working directory is used for relative remote paths. Quote wildcard patterns when they should be resolved by BaiduPCS.NET.

## Common commands

| Task | Commands |
| --- | --- |
| Accounts and settings | `account list`, `account switch UID`, `config show`, `config set cache-size=128KiB download-rate=2MiB/s` |
| Browse or manage files | `cd`, `pwd`, `list`, `tree`, `search`, `match`, `metadata`, `mkdir`, `copy`, `move`, `rename`, `remove` |
| Upload and download | `upload`, `download`, `download-links` |
| Share and recover files | `share create`, `share import`, `share list`, `share cancel`, `recycle list`, `recycle restore`, `recycle delete` |
| Manage offline tasks | `offline add`, `offline query`, `offline list`, `offline cancel`, `offline delete` |
| Export and local tools | `checksum`, `export`, `tool encrypt`, `tool decrypt`, `tool ip`, `tool time`, `run -- EXECUTABLE ARG...` |
| Check for updates | `update check`, `update apply` |

Use `--help` on a command for all options. For more examples and Go command equivalents, see [Command reference and migration](docs/commands.md).

## Configuration and release builds

`baidupcs env` shows the active profile path. Set `BAIDUPCS_CONFIG_DIR` to use a separate profile. The profile contains session credentials. Keep it in a user-private directory and keep it out of version control.

Build and run the offline fixture tests with `dotnet build` and `dotnet test`. Release archives target Windows x64 and Arm64, macOS x64 and Arm64, and Linux x64 and Arm64 on glibc and musl. Android, iOS, FreeBSD, Linux x86, and MIPS are outside the release matrix. Use the published `SHA256SUMS` to verify an archive.

Baidu can change or retire endpoints and restrict operations by account. Password sign-in, download links, rapid upload, sharing, and offline tasks depend on the currently available service. The fixture tests exercise protocol behavior. Authenticated verification with BaiduPCS-Go is a separate release gate. Standalone rapid-link import was already disabled in the pinned Go command. Checksum links and manifest export remain available for recording file metadata.

See [Architecture and API design](docs/architecture.md) for the implementation and verification details.

## License

Original BaiduPCS.NET work uses the [MIT license](LICENSE). Upstream-derived material carries the applicable BaiduPCS-Go license and attribution.
