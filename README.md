# BaiduPCS.NET

[![CI](https://github.com/ajtn123/BaiduPCS.NET/actions/workflows/ci.yml/badge.svg?branch=main&event=push)](https://github.com/ajtn123/BaiduPCS.NET/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/ajtn123/BaiduPCS.NET)](https://github.com/ajtn123/BaiduPCS.NET/releases/latest)

BaiduPCS.NET is a .NET 10 command-line client for Baidu Netdisk. It ports BaiduPCS-Go workflows to Spectre.Console.Cli and provides an interactive shell and commands for scripts. [BaiduPCS-Go](https://github.com/qjfoidnh/BaiduPCS-Go)

## Download

Choose the executable for your system. Releases provide both self-contained and framework-dependent single files. Assets with `-framework-dependent` in the filename use an installed .NET 10 runtime or roll forward to a newer compatible runtime. Self-contained assets include .NET. Check the executable's SHA-256 against the value GitHub displays in the release's Assets section. [Latest release](https://github.com/ajtn123/BaiduPCS.NET/releases/latest)

| System | Asset suffixes |
| --- | --- |
| Windows | `win-x64.exe`, `win-arm64.exe` |
| macOS | `osx-x64`, `osx-arm64` |
| Linux (glibc) | `linux-x64`, `linux-arm64` |
| Linux (musl) | `linux-musl-x64`, `linux-musl-arm64` |

On Windows, rename the file to `baidupcs.exe` and run it from PowerShell.

```powershell
.\baidupcs.exe --help
```

On macOS and Linux, rename the download to `baidupcs` and make it executable.

```sh
chmod +x baidupcs
./baidupcs --help
```

## Quick start

Put the executable on your PATH to use these examples, or use the relative path shown above. Import an existing BaiduPCS-Go profile with `account import-go`, or run `account login` to sign in interactively.

```sh
baidupcs account import-go
baidupcs account current
baidupcs quota
baidupcs ls /
```

`account import-go` reads the existing profile and writes a separate BaiduPCS.NET profile. Pass the path to `pcs_config.json` for a portable installation. `account login` prompts for the username and password. To pass existing cookies from a credential source, use `account login --cookies-stdin`. BDUSS sign-in also accepts optional PTOKEN and STOKEN.

```sh
baidupcs mkdir /example
baidupcs cd /example
baidupcs u ./notes.txt .
baidupcs d notes.txt --output-directory ./downloads
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

Use `ls` or `ll` for `list`, `d` for `download`, and `u` for `upload`. `cd` changes the remote working directory. These names work in the interactive shell and in one-shot commands. For all options and aliases, see [Command reference and migration](docs/commands.md).

Transfers in a terminal show a live table with pending, active, and finished files. Rows fill from the bottom up. When terminal height is limited, active files and the next queued files take priority. Speed averages the preceding minute and drives the ETA. Use `--raw-numbers` for exact byte counts and durations in seconds.

## Configuration

`baidupcs env` shows the active profile path. Set `BAIDUPCS_CONFIG_DIR` to use a separate profile. The profile contains session credentials. Keep it in a user-private directory and keep it out of version control.

## Baidu service compatibility

Baidu can change or retire endpoints and restrict operations by account. Assisted verification with BaiduPCS-Go 4.0.1 confirmed profile import, quota, directory and file operations, bidirectional uploads and downloads, share creation and cancellation, and recycle restoration. It also confirmed offline task listing, query, and deletion.

With the tested session, both clients failed to generate Pan web download links or import a share back into its owning account. Direct downloads worked. A repeat offline submission returned service code `36040` in both clients. Go's default upload attempt failed, then succeeded with `--norapid`. The .NET multipart upload succeeded with `--skip-rapid-check`. Sign-in challenges and account-wide clearing were checked with fixtures. Import from another account's share still awaits live verification.

Standalone rapid-link import was disabled in the pinned Go command. Checksum links and manifest export remain available for recording file metadata.

## Project guides

- Build from source, run the tests, and submit changes. [Contributing](CONTRIBUTING.md)
- Report vulnerabilities privately and protect account credentials. [Security policy](SECURITY.md)
- Read about the CLI, core API, and verification. [Architecture and API design](docs/architecture.md)
- Build and publish standalone releases. [Release guide](docs/release.md)

## License

Original BaiduPCS.NET work uses the MIT license. The notices identify the BaiduPCS-Go contributors and Apache-licensed material, and link to the issue and pull request review. [MIT license](LICENSE), [Third-party notices](THIRD-PARTY-NOTICES.md)
