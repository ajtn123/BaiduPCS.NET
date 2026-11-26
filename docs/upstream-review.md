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

Each row records the decision for the .NET port. A pending live comparison indicates that the report depends on a Baidu response or account state that still needs confirmation.

| Issue | Owner | Disposition in BaiduPCS.NET |
| --- | --- | --- |
| [#549](https://github.com/qjfoidnh/BaiduPCS-Go/issues/549) | SimpleRickSanchez | Pending live comparison. The cyclic 50–400 KB/s pattern cannot establish server throttling from transfer progress alone. |
| [#548](https://github.com/qjfoidnh/BaiduPCS-Go/issues/548) | huige007 | Pending live comparison of the post-login directory listing. The client retries read requests with a bounded timeout. |
| [#547](https://github.com/qjfoidnh/BaiduPCS-Go/issues/547) | ycc123abc | Deferred streaming discovery. Preflight detects local path collisions before starting transfers. |
| [#546](https://github.com/qjfoidnh/BaiduPCS-Go/issues/546) | ycc123abc | Pending live comparison. Session tokens remain scoped to the account and each upload target has its own checkpoint. |
| [#545](https://github.com/qjfoidnh/BaiduPCS-Go/issues/545) | duduXu | Handled by share listing. Expired records skip code lookup and failed lookups leave other rows available. |
| [#544](https://github.com/qjfoidnh/BaiduPCS-Go/issues/544) | netsonicyxf | Deferred recursive folder-size ranking. List totals cover the returned directory page set. Ranking nested directories would require a full scan. |
| [#542](https://github.com/qjfoidnh/BaiduPCS-Go/issues/542) | 2TempUser | Addressed in upload lookup. Both missing-path codes allow a new target, including files under a newly uploaded tree. |
| [#541](https://github.com/qjfoidnh/BaiduPCS-Go/issues/541) | qaz1qazlol2 | Pending release smoke checks. Publish checksums for downloaded binaries. The Go antivirus alert concerns a different executable. |
| [#540](https://github.com/qjfoidnh/BaiduPCS-Go/issues/540) | zhongll19 | Pending live comparison. An EOF during metadata lookup surfaces with the operation name and can also occur at the network layer. |
| [#533](https://github.com/qjfoidnh/BaiduPCS-Go/issues/533) | jonathanzliu | Addressed for public shares. Extraction-code verification runs when a code is supplied or embedded. |
| [#530](https://github.com/qjfoidnh/BaiduPCS-Go/issues/530) | lkfdev | Deferred a dedicated container image. The standalone Linux builds cover both glibc and musl runners and can be used in a compatible image. |
| [#529](https://github.com/qjfoidnh/BaiduPCS-Go/issues/529) | LondyGhost | Implemented. Set `network-family=ipv4` to avoid an unreachable IPv6 route. |
| [#528](https://github.com/qjfoidnh/BaiduPCS-Go/issues/528) | XNN19 | Pending live comparison. Authorization failures are distinct from transfer or integrity failures. |
| [#527](https://github.com/qjfoidnh/BaiduPCS-Go/issues/527) | Xxx91n | Addressed by the command reference, structured listing output, and documented exit codes. Deferred a separate agent skill. |
| [#525](https://github.com/qjfoidnh/BaiduPCS-Go/issues/525) | LFGN-L | Pending Arm64 publish checks. The reported long-running OpenWrt crash is specific to a Go executable. OpenWrt is outside the supported matrix. |
| [#522](https://github.com/qjfoidnh/BaiduPCS-Go/issues/522) | JerryRen471 | Transfer progress is available in the running process, including worker detail with `--worker-status`. Deferred a cross-process task list. |
| [#518](https://github.com/qjfoidnh/BaiduPCS-Go/issues/518) | TwilightSunShine | Pending live import checks. Quote a single URL or a complete copied share message. Multi-link batch invocation remains a script. |
| [#517](https://github.com/qjfoidnh/BaiduPCS-Go/issues/517) | jacobin | Implemented distinct success, operation failure, input error, and interruption exit codes. |
| [#516](https://github.com/qjfoidnh/BaiduPCS-Go/issues/516) | rfvgyhnji | Addressed. A same-name skip returns before reading the local file for checksums or parts. |
| [#515](https://github.com/qjfoidnh/BaiduPCS-Go/issues/515) | Ajingsgh | The optional `--verify` mode hashes the result before publication. A fixture checks mismatched content. |
| [#514](https://github.com/qjfoidnh/BaiduPCS-Go/issues/514) | VashStan | Addressed. The directory iterator pages past 1,000 results, including when used by recursive download. |
| [#513](https://github.com/qjfoidnh/BaiduPCS-Go/issues/513) | Goatofmountain | Pending live comparison of the download link request. The alternative link modes and server error code remain available for diagnosis. |
| [#512](https://github.com/qjfoidnh/BaiduPCS-Go/issues/512) | JouSF-1409 | Gateway HTML is reported as a protocol error. Account sign-in and Pan file operations can need different Baidu hosts. |
| [#510](https://github.com/qjfoidnh/BaiduPCS-Go/issues/510) | cmcpp | Pending live verification of account credentials. Basic login can use BDUSS, while web operations can require the Pan STOKEN. |
| [#508](https://github.com/qjfoidnh/BaiduPCS-Go/issues/508) | RuohanXiao | Pending live comparison of the affected share page. Share inspection separates opening the page from a transfer. |
| [#506](https://github.com/qjfoidnh/BaiduPCS-Go/issues/506) | linhong88 | Pending live comparison. The port uses the Pan listing request and reports its error code when parameters are rejected. |
| [#505](https://github.com/qjfoidnh/BaiduPCS-Go/issues/505) | Gerry2439 | Pending live directory upload. A service authentication error is reported for the individual file and leaves its checkpoint available. |
| [#502](https://github.com/qjfoidnh/BaiduPCS-Go/issues/502) | ctt08066-maker | Addressed structurally by recursive listing and explicit `--output-directory`. Pending live comparison for any service-side listing failure. |
| [#500](https://github.com/qjfoidnh/BaiduPCS-Go/issues/500) | Lisonwu6 | Addressed by completed-part checkpoints and bounded retries. A session rejected as expired can restart once. |
| [#499](https://github.com/qjfoidnh/BaiduPCS-Go/issues/499) | jlwang123456 | Pending live comparison of the share-page request. An inaccessible share stops before the transfer call. |
| [#498](https://github.com/qjfoidnh/BaiduPCS-Go/issues/498) | huangshilai | Pending live generation of a test share. The same extraction code is posted to Baidu and reported by the CLI. |
| [#496](https://github.com/qjfoidnh/BaiduPCS-Go/issues/496) | zjc-2905130890 | Pending live confirmation of Baidu's large-file limit. Report server code 31365 if returned. |
| [#495](https://github.com/qjfoidnh/BaiduPCS-Go/issues/495) | suntong | Adopted throttled stderr progress with quiet redirection by default. A fullscreen progress view remains deferred. |
| [#494](https://github.com/qjfoidnh/BaiduPCS-Go/issues/494) | suntong | Implemented checkpointed download ranges. Repeating the download resumes compatible partial files. |
| [#493](https://github.com/qjfoidnh/BaiduPCS-Go/issues/493) | suntong | Implemented `--verify` and `--skip-verification` plus the `verify-downloads` default. |
| [#492](https://github.com/qjfoidnh/BaiduPCS-Go/issues/492) | ZhangLeiMing1802 | Pending live comparison of a PCS token rejection after sign-in. Preserve the server code for diagnosis. |
| [#491](https://github.com/qjfoidnh/BaiduPCS-Go/issues/491) | bobo-link | Pending live confirmation of large-file finalization. Size-dependent part sizing does not establish an account-specific acceptance limit. |
| [#489](https://github.com/qjfoidnh/BaiduPCS-Go/issues/489) | trendnet-tennis | Deferred above 128 GiB until a compatible multipart endpoint and service limits can be verified. |
| [#488](https://github.com/qjfoidnh/BaiduPCS-Go/issues/488) | Nihilismer | Addressed in installation docs. The tool command is `baidupcs`, with PATH and standalone invocation instructions. |
| [#487](https://github.com/qjfoidnh/BaiduPCS-Go/issues/487) | bruse-zhang | Pending live confirmation of the account's transfer allowance. File IDs allow selection of a smaller batch if the account permits it. |
| [#486](https://github.com/qjfoidnh/BaiduPCS-Go/issues/486) | JennieZhang1999 | Missing metadata is reported as path absence or a per-item failure. Pending comparison if Baidu rejects a path that the web app shows. |
| [#485](https://github.com/qjfoidnh/BaiduPCS-Go/issues/485) | lxr-1204 | Pending live comparison of the 31365 merge response. A failed finalization retains part checkpoints. |
| [#484](https://github.com/qjfoidnh/BaiduPCS-Go/issues/484) | 362227 | Deferred alternate dynamic upload protocols. The port already supports PCS upload-host discovery and an explicit fixed host. |
| [#483](https://github.com/qjfoidnh/BaiduPCS-Go/issues/483) | chenhuping | Deferred enterprise-space switching. It needs separate service discovery and an account with both spaces. |
| [#482](https://github.com/qjfoidnh/BaiduPCS-Go/issues/482) | superdaa | Addressed by `--skip-rapid-check`, which bypasses the UK-based rapid probe. Multipart upload still needs an authorized session. |
| [#481](https://github.com/qjfoidnh/BaiduPCS-Go/issues/481) | AllIGot2020 | Pending live finalization comparison. Reaching 100% in part progress is separate from the server committing the file. |
| [#480](https://github.com/qjfoidnh/BaiduPCS-Go/issues/480) | wcjjzz | Pending live comparison. Account identification, directory listing, and share opening are separate requests with different authorization requirements. |
| [#479](https://github.com/qjfoidnh/BaiduPCS-Go/issues/479) | Lacacy | Pending comparison of the linked upload workaround against the pinned source and live multipart behavior. |
| [#477](https://github.com/qjfoidnh/BaiduPCS-Go/issues/477) | fgdsfgfdsgdsfgsdfg | The CLI reports the exact remote target after finalization. Pending live listing to confirm the reported missing-file behavior. |
| [#476](https://github.com/qjfoidnh/BaiduPCS-Go/issues/476) | renmu123 | Implemented `upload --as NAME` for one file. The selected target name is used for validation and finalization. |
| [#475](https://github.com/qjfoidnh/BaiduPCS-Go/issues/475) | CaoMeiYouRen | Removed commas from the optional upload name restriction. |
| [#473](https://github.com/qjfoidnh/BaiduPCS-Go/issues/473) | ctt08066-maker | Separate upload sessions and part checksums protect file identity. Pending a reproducible case for incorrect remote content. |
| [#471](https://github.com/qjfoidnh/BaiduPCS-Go/issues/471) | nicole-coco | Use quoted remote wildcard paths. A named include filter for nested contents remains deferred. |
| [#469](https://github.com/qjfoidnh/BaiduPCS-Go/issues/469) | ai30328348 | Pending live comparison of direct, PCS, and streaming link modes. Membership alone does not guarantee client-side throughput. |
| [#468](https://github.com/qjfoidnh/BaiduPCS-Go/issues/468) | ai30328348 | Use stdout for outcomes and stderr for errors and verbose progress. Shell redirection can retain both streams for a background job. |
| [#467](https://github.com/qjfoidnh/BaiduPCS-Go/issues/467) | ZEntrosein | Implemented per-file upload conflict policy inside matching directory trees. Share transfer merge behavior stays with Baidu. |
| [#465](https://github.com/qjfoidnh/BaiduPCS-Go/issues/465) | Alotofwater | Implemented. Upload failures use stderr and make the command return a failure status. |
| [#464](https://github.com/qjfoidnh/BaiduPCS-Go/issues/464) | leewensong | Pending live comparison of share metadata retrieval with a known authorized share and account. |
| [#463](https://github.com/qjfoidnh/BaiduPCS-Go/issues/463) | Eaglex129 | Completed parts survive retries in the same session. A service-expired upload session can cause progress to restart once. |
| [#460](https://github.com/qjfoidnh/BaiduPCS-Go/issues/460) | lcdyk0517 | The UK lookup can fall back to multipart precreate and be skipped explicitly. Connectivity failures remain visible on upload operations. |
| [#458](https://github.com/qjfoidnh/BaiduPCS-Go/issues/458) | EvthPst | Pending a reproducible upload with current endpoints. Retries and final operation failures are bounded and reported per target. |
| [#456](https://github.com/qjfoidnh/BaiduPCS-Go/issues/456) | yuyemin | Pending live comparison of the BDUSS sign-in timeout. Browser and CLI sign-in may take different service routes. |
| [#455](https://github.com/qjfoidnh/BaiduPCS-Go/issues/455) | summersohot | Implemented optional validation before publishing downloads. For untrusted service MD5, compare an independently known digest. |
| [#451](https://github.com/qjfoidnh/BaiduPCS-Go/issues/451) | 897601689 | Pending live observation. The upload rate cap is adjustable and reports a client ceiling, while network or server changes can lower throughput. |
| [#449](https://github.com/qjfoidnh/BaiduPCS-Go/issues/449) | 362227 | Pending live upload and relisting. A reported successful finalization can only be confirmed against the same account and remote path. |
| [#447](https://github.com/qjfoidnh/BaiduPCS-Go/issues/447) | lianglunping | Addressed for expired sessions. A 31061 finalization response can restart the multipart upload once. |
| [#446](https://github.com/qjfoidnh/BaiduPCS-Go/issues/446) | gzm2015 | Redirected progress is quiet by default. Completed-part checkpoints persist, with a possible restart on an expired session. |
| [#443](https://github.com/qjfoidnh/BaiduPCS-Go/issues/443) | 362227 | Pending live large-file validation. Large uploads still hash required parts, and `--skip-rapid-check` skips the rapid probe. |
| [#441](https://github.com/qjfoidnh/BaiduPCS-Go/issues/441) | bu-code | Use `search --json` to obtain full paths and pass reviewed paths to `remove` or `move`. Deferred combined search and mutation. |
| [#437](https://github.com/qjfoidnh/BaiduPCS-Go/issues/437) | sanwecn | Implemented the equivalent option as `upload --skip-rapid-check`. |
| [#428](https://github.com/qjfoidnh/BaiduPCS-Go/issues/428) | SeeFlowerX | Supported recognized Baidu share URL forms. Pending live comparison of the specific link variant. |
| [#423](https://github.com/qjfoidnh/BaiduPCS-Go/issues/423) | Qmumu | Deferred server-side extraction. Archive extraction requires a separate service endpoint and feature eligibility. |
| [#420](https://github.com/qjfoidnh/BaiduPCS-Go/issues/420) | Jason-2048 | Deferred recursive folder sizes in `tree`. The depth option bounds current traversal and `list` totals immediate files. |
| [#418](https://github.com/qjfoidnh/BaiduPCS-Go/issues/418) | bugskill | Pending live flow comparing identity, quota, listing, and metadata. An account label alone cannot establish file API access. |
| [#416](https://github.com/qjfoidnh/BaiduPCS-Go/issues/416) | 444026042 | Use the standalone CLI with an operating-system job manager. Verbose stderr progress can be redirected to a log. |
| [#414](https://github.com/qjfoidnh/BaiduPCS-Go/issues/414) | bladeofgod | Cookie parsing is independent of the Go warning. Pending a sanitized reproduction if quoted cookie values impede a .NET transfer. |
| [#411](https://github.com/qjfoidnh/BaiduPCS-Go/issues/411) | bladeofgod | Pending cross-shell smoke checks. Quote paths at the shell boundary and inspect the CLI exit status. |
| [#410](https://github.com/qjfoidnh/BaiduPCS-Go/issues/410) | aa827508 | Pending live cookie verification. The browser's Pan STOKEN and page session token serve different purposes. |
| [#408](https://github.com/qjfoidnh/BaiduPCS-Go/issues/408) | asasdsaweasd | Pending share listing with live accounts. Per-share code lookup failure is isolated from the remaining records. |
| [#405](https://github.com/qjfoidnh/BaiduPCS-Go/issues/405) | huangshilai | Pending live comparison. Interactive sign-in uses masked input and accounts are persisted after validation. |
| [#404](https://github.com/qjfoidnh/BaiduPCS-Go/issues/404) | sylingd | Part uploads stream and the response-header wait is bounded independently of the complete transfer. Deferred explicit part-size tuning. |
| [#399](https://github.com/qjfoidnh/BaiduPCS-Go/issues/399) | WolfLC | Pending share-page comparison with an approved live sample. Open, extraction-code verification, and transfer are separate stages. |
| [#397](https://github.com/qjfoidnh/BaiduPCS-Go/issues/397) | wahahaer | Deferred recursive size for metadata. Server metadata and the sizes of contained files would otherwise be conflated. |
| [#396](https://github.com/qjfoidnh/BaiduPCS-Go/issues/396) | 384581169 | Use `share inspect` to find file IDs and `share import --file-id` to choose specific entries. |
| [#391](https://github.com/qjfoidnh/BaiduPCS-Go/issues/391) | KNaiFen | Deferred automatic local deletion after upload. It needs a data-loss and verification design, particularly for skipped files. |
| [#390](https://github.com/qjfoidnh/BaiduPCS-Go/issues/390) | WolfLC | Architecture docs locate the HTTP transport and README documents building the tool. Proxy configuration covers the standard routing case. |
| [#385](https://github.com/qjfoidnh/BaiduPCS-Go/issues/385) | yqs112358 | Implemented `--json` for list, metadata, and search with full structured entries. |
| [#384](https://github.com/qjfoidnh/BaiduPCS-Go/issues/384) | dave2nian | Implemented `--skip-rapid-check` on upload. Required multipart checksums are still calculated. |
| [#378](https://github.com/qjfoidnh/BaiduPCS-Go/issues/378) | code-yuan-dev | Implemented read-only `share inspect` for public and code-protected shares. |
| [#371](https://github.com/qjfoidnh/BaiduPCS-Go/issues/371) | moerrrr | Share listing isolates lookup failures and reports service errors through the CLI. Pending a live reproduction of the reported abrupt exit. |
| [#356](https://github.com/qjfoidnh/BaiduPCS-Go/issues/356) | hnqyljl | Implemented IPv4-only mode for failed IPv6 connections, with auto routing as the default. |
| [#355](https://github.com/qjfoidnh/BaiduPCS-Go/issues/355) | narfa0215 | Deferred remote MD5 rewriting. Local and export checksum commands can calculate digests without modifying the remote file. |
| [#354](https://github.com/qjfoidnh/BaiduPCS-Go/issues/354) | guoxk | Transport includes the configured app ID with service requests. Pending live comparison if Baidu reports it missing. |
| [#353](https://github.com/qjfoidnh/BaiduPCS-Go/issues/353) | JH6588 | Deferred group file APIs, which use a separate Baidu sharing surface. |
| [#352](https://github.com/qjfoidnh/BaiduPCS-Go/issues/352) | joe12801 | Link discovery reports link mode and relevant user agent. External downloaders must still satisfy any session or expiration requirements. |
| [#351](https://github.com/qjfoidnh/BaiduPCS-Go/issues/351) | JackySu | The download engine validates missing links and failed ranges as errors. The reported large-file crash needs a reproducible response. |
| [#350](https://github.com/qjfoidnh/BaiduPCS-Go/issues/350) | daxiaamo | Pending live upload host and part comparison. Host discovery and a fixed override allow testing a rejected cluster response. |
| [#346](https://github.com/qjfoidnh/BaiduPCS-Go/issues/346) | ghost | Invalid service JSON is reported without treating the transfer as successful. Pending reproduction of the particular response. |
| [#345](https://github.com/qjfoidnh/BaiduPCS-Go/issues/345) | bloomachine | Zero-byte downloads are valid only for genuinely empty remote files. Failed paths or download requests produce an error and status. |
| [#343](https://github.com/qjfoidnh/BaiduPCS-Go/issues/343) | asasdsaweasd | Redirected stderr is quiet during upload unless verbose mode is requested. Interactive progress can still be redirected. |
| [#342](https://github.com/qjfoidnh/BaiduPCS-Go/issues/342) | calyle | Deferred an undocumented document-preview path for downloads. The port uses the documented command modes inherited from the Go client. |
| [#339](https://github.com/qjfoidnh/BaiduPCS-Go/issues/339) | reidmex | Pending live large upload on the affected service. The engine bounds retries and checkpoints successful parts. |
| [#336](https://github.com/qjfoidnh/BaiduPCS-Go/issues/336) | stefanb | Go compiler linkage incompatibility. The port relies on the .NET SDK and runtime. |
| [#335](https://github.com/qjfoidnh/BaiduPCS-Go/issues/335) | leeivan | A compatible checkpoint resumes, and a rejected session can be restarted once, depending on the returned error code. |
| [#333](https://github.com/qjfoidnh/BaiduPCS-Go/issues/333) | snchrome | Pending live throughput comparison. The report concerns a separate web frontend and a router install. |
| [#330](https://github.com/qjfoidnh/BaiduPCS-Go/issues/330) | jiangjiangflies | Deferred deletion-based synchronization. Upload supports incremental replacement by name and size. |
| [#329](https://github.com/qjfoidnh/BaiduPCS-Go/issues/329) | ititchuan | Deferred an rclone backend. The CLI can provide structured listings that scripts can consume. |
| [#328](https://github.com/qjfoidnh/BaiduPCS-Go/issues/328) | zhxsxuan | Pending live rapid-upload coverage. The port falls back to multipart upload when a rapid probe fails. |
| [#327](https://github.com/qjfoidnh/BaiduPCS-Go/issues/327) | 5780358 | Pending live share creation. The PCS host setting controls PCS requests and leaves the Pan sharing endpoint on its own host. |
| [#326](https://github.com/qjfoidnh/BaiduPCS-Go/issues/326) | 34Hz-com | Implemented `download-prefix=` for the save directory without an account subdirectory. |
| [#324](https://github.com/qjfoidnh/BaiduPCS-Go/issues/324) | 5780358 | Pending live token validity check. Account-specific credentials can be refreshed through sign-in or legacy import. |
| [#323](https://github.com/qjfoidnh/BaiduPCS-Go/issues/323) | gavinwang668 | Pending account sign-in comparison. System busy is surfaced as a service failure with the returned code. |
| [#322](https://github.com/qjfoidnh/BaiduPCS-Go/issues/322) | cangyuegui | Retain original copyright notices and add port attribution in the distributed license materials. |
| [#321](https://github.com/qjfoidnh/BaiduPCS-Go/issues/321) | daxiaamo | Pending long-transfer reproduction. The port bounds workers and retries and retains confirmed parts. |
| [#319](https://github.com/qjfoidnh/BaiduPCS-Go/issues/319) | StrollStars | Use `match` for remote glob expansion and `export` for a recursive directory inventory. Search uses the Baidu keyword API. |
| [#318](https://github.com/qjfoidnh/BaiduPCS-Go/issues/318) | proItheus | Implemented `share import --download` using the paths Baidu returns after import. Missing paths yield a failure for the download request. |
| [#316](https://github.com/qjfoidnh/BaiduPCS-Go/issues/316) | yayo | Addressed by short, hashed staging filenames independent of the remote basename. |
| [#314](https://github.com/qjfoidnh/BaiduPCS-Go/issues/314) | starpickuper | Pending rapid-probe live verification. Refreshing an access token alone does not guarantee a server-side match. |
| [#313](https://github.com/qjfoidnh/BaiduPCS-Go/issues/313) | a9udn9u | Deferred a preallocation option. Sequential fallback writes progressively. Parallel range downloads size the stage to allow positioned writes. |
| [#312](https://github.com/qjfoidnh/BaiduPCS-Go/issues/312) | nations-Xiong | Deferred sync-space and mount integration. Regular drive navigation remains available through the interactive shell. |
| [#311](https://github.com/qjfoidnh/BaiduPCS-Go/issues/311) | Aarchbishop | `list` summarizes immediate file and directory counts. Deferred full recursive counts for arbitrary directories. |
| [#310](https://github.com/qjfoidnh/BaiduPCS-Go/issues/310) | NNNNAI | Pending a comparison of the same download and link mode. An official-client throughput figure does not determine a third-party link's capacity. |
| [#309](https://github.com/qjfoidnh/BaiduPCS-Go/issues/309) | NNNNAI | Use `share inspect` to identify files, then `share import --download`. Direct downloads from a third party's share remain deferred. |
| [#308](https://github.com/qjfoidnh/BaiduPCS-Go/issues/308) | VJ852 | The typed core and CLI JSON are the available automation surfaces. Deferred a separate Python client and upload event watcher. |
| [#307](https://github.com/qjfoidnh/BaiduPCS-Go/issues/307) | LoserForever88 | Pending live comparison of rapid eligibility. A missed rapid match can still complete through the multipart upload path. |
| [#306](https://github.com/qjfoidnh/BaiduPCS-Go/issues/306) | 362227 | Pending live comparison for 31352. Finalization reports the remote target and service error, preserving the checkpoint. |
| [#303](https://github.com/qjfoidnh/BaiduPCS-Go/issues/303) | joe12801 | Pending live share import comparison. The report supplies no sanitized metadata response for a fixture. |
| [#300](https://github.com/qjfoidnh/BaiduPCS-Go/issues/300) | cathy-imu | Deferred extraction of generated playback subtitles. It requires a separate playback API and membership capability. |
| [#289](https://github.com/qjfoidnh/BaiduPCS-Go/issues/289) | turtoe | Implemented transferred bytes, speed, and estimated time on the terminal, with throttled updates. |
| [#288](https://github.com/qjfoidnh/BaiduPCS-Go/issues/288) | limitless-share | Pending live comparison of share metadata retrieval across accounts. |
| [#287](https://github.com/qjfoidnh/BaiduPCS-Go/issues/287) | ChitaGideon | Deferred converting third-party shares into rapid links without importing. The port exposes share inventory and export as separate operations. |
| [#286](https://github.com/qjfoidnh/BaiduPCS-Go/issues/286) | damoncui668 | Password entry is masked, the sign-in transport uses HTTPS, and sensitive responses are excluded from diagnostics. |
| [#283](https://github.com/qjfoidnh/BaiduPCS-Go/issues/283) | yangfl | Pending comparable link and failure information for the old 3.9.2 report. Current downloads validate ranges and completion before publication. |
| [#282](https://github.com/qjfoidnh/BaiduPCS-Go/issues/282) | 362227 | Implemented `share create --code CODE --days DAYS`. |
| [#281](https://github.com/qjfoidnh/BaiduPCS-Go/issues/281) | jacklee789 | Recognized Baidu short and legacy long share URLs are supported by the common parser. |
| [#280](https://github.com/qjfoidnh/BaiduPCS-Go/issues/280) | WQY916 | Documented the upstream inactive rapid-link import and preserved checksum-link export. Deferred new import support. |
| [#279](https://github.com/qjfoidnh/BaiduPCS-Go/issues/279) | bensjohn | Implemented stable release discovery and exact-runtime assets with checksum verification. Connectivity to the release host is still required. |
| [#278](https://github.com/qjfoidnh/BaiduPCS-Go/issues/278) | bigmouse0001 | Implemented explicit export filenames through `--output`. |
| [#277](https://github.com/qjfoidnh/BaiduPCS-Go/issues/277) | linktomarkdown | The typed core can be referenced from source. Deferred an independently supported SDK package. |
| [#276](https://github.com/qjfoidnh/BaiduPCS-Go/issues/276) | yoyosbg | Keep Baidu's deletion authorization requirements. The port reports the service rejection. |
| [#275](https://github.com/qjfoidnh/BaiduPCS-Go/issues/275) | williamjieh | Deferred browsing and downloading files from Baidu group libraries. |
| [#274](https://github.com/qjfoidnh/BaiduPCS-Go/issues/274) | 362227 | Deferred writing the official client's private cache format. Structured remote listings are available for a user-owned index. |
| [#273](https://github.com/qjfoidnh/BaiduPCS-Go/issues/273) | doubilbm | Completed-part checkpoints and bounded retries address repeated progress resets. An expired session can restart once. |
| [#272](https://github.com/qjfoidnh/BaiduPCS-Go/issues/272) | ghost | Profile, shell history, and upload checkpoint locations are documented. Download checkpoints live beside their staged files. |
| [#271](https://github.com/qjfoidnh/BaiduPCS-Go/issues/271) | yayo | Implemented explicit single-file naming with `upload --as NAME` while keeping the positional destination directory. |
| [#270](https://github.com/qjfoidnh/BaiduPCS-Go/issues/270) | yayo | Implemented `share import --destination PATH`. |
| [#268](https://github.com/qjfoidnh/BaiduPCS-Go/issues/268) | yayo | No separate unsplit upload mode. Multipart parts stream with .NET transport and the configured rate limit. |
| [#266](https://github.com/qjfoidnh/BaiduPCS-Go/issues/266) | daxiaamo | Implemented per-invocation `--skip-rapid-check`. Deferred adding another persistent toggle. |
| [#265](https://github.com/qjfoidnh/BaiduPCS-Go/issues/265) | newnames | Expired upload sessions are invalidated and can restart once. Other service failures remain visible. |
| [#264](https://github.com/qjfoidnh/BaiduPCS-Go/issues/264) | daiaji | Deferred the third-party acceleration utility. Use documented link modes and adjustable concurrency. |
| [#263](https://github.com/qjfoidnh/BaiduPCS-Go/issues/263) | Raywoo1 | TLS timeout is reported through bounded metadata requests. IPv4 selection, proxy settings, and local binding cover supported routing controls. |
| [#262](https://github.com/qjfoidnh/BaiduPCS-Go/issues/262) | QGB | Unreadable files are reported and remaining enumerated files continue. The batch returns failure if anything was skipped due to access errors. |
| [#261](https://github.com/qjfoidnh/BaiduPCS-Go/issues/261) | stark131 | Progress updates are throttled. Deferred customizable intervals, since normal redirected output already omits progress. |
| [#259](https://github.com/qjfoidnh/BaiduPCS-Go/issues/259) | 362227 | Individual sizes are available in `list --json` and `metadata --json`. Deferred duplicating them in the tree display. |
| [#257](https://github.com/qjfoidnh/BaiduPCS-Go/issues/257) | Left024 | Implemented `list --json` for scripting. |
| [#256](https://github.com/qjfoidnh/BaiduPCS-Go/issues/256) | 362227 | Upload part checksums and explicit finalization protect the transfer. Pending live comparison if Baidu reports a zero-byte result for nonempty input. |
| [#255](https://github.com/qjfoidnh/BaiduPCS-Go/issues/255) | SinoHan | Deferred mirror deletion. Size-based overwrite can update and add files without deleting unmatched remote content. |
| [#254](https://github.com/qjfoidnh/BaiduPCS-Go/issues/254) | daiaji | Implemented `share create --combined`, including the extraction code. Clipboard integration remains deferred. |
| [#253](https://github.com/qjfoidnh/BaiduPCS-Go/issues/253) | qwsxmkoi | Legacy ARMv7 packaging is outside the release matrix. Arm64 Linux self-contained artifacts cover the supported 64-bit environments. |
| [#251](https://github.com/qjfoidnh/BaiduPCS-Go/issues/251) | daiaji | Metadata comes from Baidu and can differ from file content. Local `checksum` measures actual bytes. |
| [#250](https://github.com/qjfoidnh/BaiduPCS-Go/issues/250) | TensorABC | Export traverses directories and obtains required checksums. Deferred changing rate or concurrency without a reproducible bottleneck. |
| [#249](https://github.com/qjfoidnh/BaiduPCS-Go/issues/249) | wtwangcb | Documented the inactive rapid-link replay command. Export still writes the interoperable link format where checksum data exists. |
| [#247](https://github.com/qjfoidnh/BaiduPCS-Go/issues/247) | daiaji | Deferred glob negation. The existing matcher supports explicit paths, `*`, and `?`. |
| [#242](https://github.com/qjfoidnh/BaiduPCS-Go/issues/242) | arstum | Deferred subscribed shares. Controls for account-created shares and URL-based imports are available. |
| [#240](https://github.com/qjfoidnh/BaiduPCS-Go/issues/240) | proItheus | Repeat the original download to use its checkpoints. Deferred all-download resumption without paths and a centralized job store. |
| [#238](https://github.com/qjfoidnh/BaiduPCS-Go/issues/238) | beiank | `--verbose` provides diagnostics. Use authenticated legacy import or masked input to supply existing credentials without printing them. |
| [#237](https://github.com/qjfoidnh/BaiduPCS-Go/issues/237) | hzhangxyz | Preserved upstream's treatment of empty files. Supporting empty remote objects needs separate live service validation. |
| [#236](https://github.com/qjfoidnh/BaiduPCS-Go/issues/236) | hzhangxyz | Addressed. Traversal follows valid links and skips broken links or cycles. |
| [#233](https://github.com/qjfoidnh/BaiduPCS-Go/issues/233) | cainslayer | Go runtime error on Termux. The reviewed .NET distribution matrix covers desktop and supported Linux RIDs. |
| [#232](https://github.com/qjfoidnh/BaiduPCS-Go/issues/232) | lihanchen | The upload checks existing paths before hashing and prevents duplicate targets within a batch. Pending live reproduction at the reported volume. |
| [#231](https://github.com/qjfoidnh/BaiduPCS-Go/issues/231) | joe12801 | Pending live comparison of share creation and import. Each operation reports its own service error and stage. |
| [#230](https://github.com/qjfoidnh/BaiduPCS-Go/issues/230) | joe12801 | Deferred pattern-based batch rename. Explicit `rename` and structured listings can support a reviewed script. |
| [#229](https://github.com/qjfoidnh/BaiduPCS-Go/issues/229) | youlanying | Deferred QR sign-in. The pinned CLI's password challenges, cookies, and token sign-in cover the port's baseline. |
| [#228](https://github.com/qjfoidnh/BaiduPCS-Go/issues/228) | lee850220 | Implemented page selection for `share list`. Deferred automatically fetching all pages and per-record extraction codes in one command. |
| [#227](https://github.com/qjfoidnh/BaiduPCS-Go/issues/227) | Heclgg | Pending Linux Arm64 checks and live throughput comparison. Orange Pi board behavior depends on its operating system runtime support. |
| [#226](https://github.com/qjfoidnh/BaiduPCS-Go/issues/226) | qunlan | Deferred list row numbers as target selectors. Use stable full paths from JSON and file IDs where the receiving command accepts them. |
| [#225](https://github.com/qjfoidnh/BaiduPCS-Go/issues/225) | ownerowner | Pending a reproducible response. Transfer content is written as bytes and invalid service responses are handled separately. |
| [#224](https://github.com/qjfoidnh/BaiduPCS-Go/issues/224) | 123485k | Implemented separate download and upload bandwidth caps. |
| [#219](https://github.com/qjfoidnh/BaiduPCS-Go/issues/219) | Mw-HbCK | Implemented `upload --parallel`, `--file-concurrency`, and the corresponding profile defaults. |
| [#218](https://github.com/qjfoidnh/BaiduPCS-Go/issues/218) | newyue588cc | Pending live sign-in challenge comparison. Unsupported HTML responses surface as protocol errors without dumping their bodies. |
| [#216](https://github.com/qjfoidnh/BaiduPCS-Go/issues/216) | Amarillys | The CLI offers masked interactive sign-in and explicit cookie or token input. Pending terminal smoke verification on Windows Server. |
| [#215](https://github.com/qjfoidnh/BaiduPCS-Go/issues/215) | haobinnan | Deferred bulk replay because the pinned baseline's standalone rapid-link import is inactive. |
| [#214](https://github.com/qjfoidnh/BaiduPCS-Go/issues/214) | yangmeishu | Deferred web UI and HTTP management. The typed core and CLI are available. |
| [#212](https://github.com/qjfoidnh/BaiduPCS-Go/issues/212) | denniszzg | Downloads skip existing destinations by default. Replacement requires `--overwrite`. |
| [#210](https://github.com/qjfoidnh/BaiduPCS-Go/issues/210) | rsj123 | The reported crash occurs in Go DNS lookup. The .NET transport uses platform resolution and reports connection failures. |
| [#206](https://github.com/qjfoidnh/BaiduPCS-Go/issues/206) | Myriad-Dreamin | The typed core is usable as a project reference. A separate NuGet SDK is deferred. |
| [#204](https://github.com/qjfoidnh/BaiduPCS-Go/issues/204) | hangcidu | The one-shot CLI inherits terminal and redirected streams. Pending a reproduction of the reported screen-specific failure. |
| [#203](https://github.com/qjfoidnh/BaiduPCS-Go/issues/203) | hulu204 | Pending live comparison of rate caps and link modes for an affected account. Client settings cannot raise Baidu's account allowance. |
| [#199](https://github.com/qjfoidnh/BaiduPCS-Go/issues/199) | kdurov | Kept versioned release assets to match checksum and updater discovery. `update check` identifies the stable version. |
| [#198](https://github.com/qjfoidnh/BaiduPCS-Go/issues/198) | shenwii | Deferred stdin streaming. Precreate requires the file length and hashes and resume requires a stable source. |
| [#197](https://github.com/qjfoidnh/BaiduPCS-Go/issues/197) | zzb0 | The screenshot-only issue lacks a reproducible request or error description. Pending actionable detail. |
| [#195](https://github.com/qjfoidnh/BaiduPCS-Go/issues/195) | mlsub | Windows Arm64 is included in the standalone release matrix. Confirm by running the native artifact. |
| [#194](https://github.com/qjfoidnh/BaiduPCS-Go/issues/194) | albb762 | Go programs can launch `baidupcs` as a subprocess and use its exit status and JSON output. |
| [#192](https://github.com/qjfoidnh/BaiduPCS-Go/issues/192) | linhandev | Per-file progress includes bytes completed and total. Deferred positional task counts, which would be ambiguous across concurrent files. |
| [#191](https://github.com/qjfoidnh/BaiduPCS-Go/issues/191) | zzb0 | Invalid paths, unusable links, and transfer failures surface as a specific error or per-file status. Pending a reproduction of the reported early exit. |
| [#188](https://github.com/qjfoidnh/BaiduPCS-Go/issues/188) | Pete-Young | Pending a reproduction on the reported filesystem. Sequential transfer is available for sources without range support. |
| [#187](https://github.com/qjfoidnh/BaiduPCS-Go/issues/187) | boyodyang | User agents can be configured separately for PCS, Pan, and the default request context. |
| [#184](https://github.com/qjfoidnh/BaiduPCS-Go/issues/184) | boyodyang | Existing files are normally skipped, distinct from failed files. The old crash log needs a current reproduction. |
| [#182](https://github.com/qjfoidnh/BaiduPCS-Go/issues/182) | waiav123 | Pending live share creation against the returned parameter error. The CLI validates the extraction code and nonnegative expiry. |
| [#181](https://github.com/qjfoidnh/BaiduPCS-Go/issues/181) | 123485k | Deferred a new sync engine with dry-run and mirror semantics. The upload conflict policies cover basic incremental replacement. |
| [#179](https://github.com/qjfoidnh/BaiduPCS-Go/issues/179) | moox2020 | Deferred group library traversal and import. It requires group-specific APIs rather than public share URLs. |
| [#174](https://github.com/qjfoidnh/BaiduPCS-Go/issues/174) | hanksuper | Failures report the path and error and the final exit status. Deferred grouped summaries until script output needs a dedicated schema. |
| [#173](https://github.com/qjfoidnh/BaiduPCS-Go/issues/173) | jyuan15 | Upload precreate and finalization are separate and existing targets follow explicit conflict policy. Pending live reproduction of zero-byte placeholders. |
| [#172](https://github.com/qjfoidnh/BaiduPCS-Go/issues/172) | Jieoz | Pending live comparison of 31326. Preserve the service authorization denial and do not treat it as a transfer success. |
| [#170](https://github.com/qjfoidnh/BaiduPCS-Go/issues/170) | mir355 | Deferred recursive ignore-file syntax. Pass explicitly selected local paths when excluding subtrees. |
| [#163](https://github.com/qjfoidnh/BaiduPCS-Go/issues/163) | Niuyongchao | Pending live comparison. Link candidates and configured worker count can be inspected without assuming a throughput floor. |
| [#159](https://github.com/qjfoidnh/BaiduPCS-Go/issues/159) | sonataaaa | Multiple paths and wildcards work in one download. Deferred a persistent independent queue. |
| [#158](https://github.com/qjfoidnh/BaiduPCS-Go/issues/158) | jiangjiangflies | Filename prechecks are optional in configuration. Commas are accepted in the checked mode. |
| [#148](https://github.com/qjfoidnh/BaiduPCS-Go/issues/148) | Naivedevil | Pending live speed comparison with equivalent files and account. Upload caps and worker concurrency are configurable. |
| [#147](https://github.com/qjfoidnh/BaiduPCS-Go/issues/147) | Jieoz | Sequential fallback handles sources that reject ranges. Worker errors surface as transfer failures after bounded retries. |
| [#144](https://github.com/qjfoidnh/BaiduPCS-Go/issues/144) | jimbos | Implemented upload conflict policy `overwrite-if-size-differs` for incremental replacement by size. |
| [#141](https://github.com/qjfoidnh/BaiduPCS-Go/issues/141) | wtwangcb | Implemented `download --preserve-mtime`. |
| [#139](https://github.com/qjfoidnh/BaiduPCS-Go/issues/139) | Yarke99 | Implemented share inventory plus file-ID selection on import. Download after import is supported. Direct third-party links remain deferred. |
| [#138](https://github.com/qjfoidnh/BaiduPCS-Go/issues/138) | ghost | Go synchronization panic. The .NET upload scheduler and checkpoint serialization use separate runtime primitives. |
| [#130](https://github.com/qjfoidnh/BaiduPCS-Go/issues/130) | maxadc | Pending live BDUSS validation against the account route. The old timeout concerns the Go client's Tieba request. |
| [#107](https://github.com/qjfoidnh/BaiduPCS-Go/issues/107) | zhiting9420 | Termux resolver configuration is outside the release matrix. The port uses platform DNS on supported hosts. |
| [#92](https://github.com/qjfoidnh/BaiduPCS-Go/issues/92) | pongfcnkl | Pending comparison with the entitled account and the same link mode on multiple operating systems. HTTP 403 remains a service failure. |
| [#90](https://github.com/qjfoidnh/BaiduPCS-Go/issues/90) | zhaobinGitHub | Directory paging limits each request and detects stalled pages. Pending a live large-directory performance sample. |
| [#89](https://github.com/qjfoidnh/BaiduPCS-Go/issues/89) | boyodyang | Whole-tree downloads scan before transferring to detect path conflicts. Large targets can be split across multiple invocations. |
| [#87](https://github.com/qjfoidnh/BaiduPCS-Go/issues/87) | GeorgeJoestar | Android DNS and hotspot behavior are outside the supported matrix. Supported clients expose proxy, bind, and address-family settings. |
| [#76](https://github.com/qjfoidnh/BaiduPCS-Go/issues/76) | nazhendenb | Pending login verification. The report identifies a header timeout without enough detail to isolate the failing endpoint. |
| [#2](https://github.com/qjfoidnh/BaiduPCS-Go/issues/2) | ws4liu | Pending live comparison of 31526. The port reports the account authorization error and ends the download for that file. |
