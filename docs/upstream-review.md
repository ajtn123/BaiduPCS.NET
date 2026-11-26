# Open BaiduPCS-Go review

The November 26, 2026 snapshot contains 16 open pull requests and 222 open issues in [qjfoidnh/BaiduPCS-Go](https://github.com/qjfoidnh/BaiduPCS-Go). The port baseline is `1b9131817aaf8ca7dee24bc00e33ebc4c7a5cc73`. Review decisions refer to the .NET port and do not change upstream GitHub records.

## Pull requests

| Pull request | Owner | Disposition in BaiduPCS.NET |
| --- | --- | --- |
| [#551](https://github.com/qjfoidnh/BaiduPCS-Go/pull/551) Prefer IPv6 | Jackson2z | Ported as `network-family=prefer-ipv6` with IPv4 fallback. `network-family=ipv4` also addresses networks with broken IPv6. Address ordering is fixture-tested. |
| [#550](https://github.com/qjfoidnh/BaiduPCS-Go/pull/550) Read-only shared-file inventory | victorwoo | Ported as `share inspect`. It returns a JSON inventory, walks shared directories with bounded paging, and never calls the transfer endpoint. The public-share inventory fixture checks the request flow. |
| [#537](https://github.com/qjfoidnh/BaiduPCS-Go/pull/537) Public shares without extraction codes | victorwoo | Supported by the shared link-opening logic. The client requests extraction-code verification only when one is supplied or embedded. |
| [#532](https://github.com/qjfoidnh/BaiduPCS-Go/pull/532) Read-only mount | xjackzenvey | Deferred. A filesystem mount introduces an operating-system driver and platform-specific installation and support requirements outside the command-line port. |
| [#520](https://github.com/qjfoidnh/BaiduPCS-Go/pull/520) Automatic split transfers and VLC playback | makeit2008 | Deferred. Share import supports explicit file IDs and reports Baidu's service limits. VLC playback would add a separate player integration and runtime dependency. |
| [#497](https://github.com/qjfoidnh/BaiduPCS-Go/pull/497) Live progress dashboard | suntong | Adopted throttled transfer progress on stderr and quiet redirected operation. Deferred the fullscreen dashboard so output remains compatible with the shell and scripts. |
| [#403](https://github.com/qjfoidnh/BaiduPCS-Go/pull/403) Update `golang.org/x/crypto` | dependabot[bot] | Go dependency maintenance. The port uses .NET cryptography and is covered by known-answer and interoperability tests. |
| [#402](https://github.com/qjfoidnh/BaiduPCS-Go/pull/402) Update `golang.org/x/sys` | dependabot[bot] | Go dependency maintenance. The port uses .NET filesystem and networking APIs. |
| [#392](https://github.com/qjfoidnh/BaiduPCS-Go/pull/392) Configurable save prefix | phanen | Ported as `download-prefix`, including a shared relative directory or removal of the account prefix. The setting accepts paths and has an explicit `@account` reset value. |
| [#380](https://github.com/qjfoidnh/BaiduPCS-Go/pull/380) Bash menu | dave2nian | Deferred. The portable shell already provides contextual help, command completion, and remote path completion. |
| [#376](https://github.com/qjfoidnh/BaiduPCS-Go/pull/376) Update Go tablewriter | dependabot[bot] | Go dependency maintenance. Spectre.Console renders tables in the port. |
| [#375](https://github.com/qjfoidnh/BaiduPCS-Go/pull/375) Update `urfave/cli` | dependabot[bot] | Go dependency maintenance. Spectre.Console.Cli handles command parsing and help. |
| [#374](https://github.com/qjfoidnh/BaiduPCS-Go/pull/374) Update Go runewidth | dependabot[bot] | Go dependency maintenance. Spectre.Console measures terminal display width. |
| [#320](https://github.com/qjfoidnh/BaiduPCS-Go/pull/320) Gin HTTP API | wuzhican | Deferred. The typed core and CLI are available. Exposing account operations through an HTTP server would require its own authentication and deployment design. |
| [#213](https://github.com/qjfoidnh/BaiduPCS-Go/pull/213) Android cgo DNS support | rhjdvsgsgks | Go/Termux packaging fix outside the desktop/server .NET release matrix. |
| [#205](https://github.com/qjfoidnh/BaiduPCS-Go/pull/205) Ignore symlink traversal errors | James4Ever0 | The port follows valid links, skips broken links and cycles, and reports other access failures. It handles the symlink case without suppressing unrelated errors. |

## Issues

The open issue inventory is captured at the same cutoff. Individual issue dispositions are in progress.
