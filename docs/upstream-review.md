# BaiduPCS-Go 未关闭议题评审

2026 年 11 月 26 日的上游快照包含 16 个未关闭的拉取请求和 222 个未关闭的议题。移植基线为 `1b9131817aaf8ca7dee24bc00e33ebc4c7a5cc73`。表中状态记录 BaiduPCS.NET 的移植决定。 [qjfoidnh/BaiduPCS-Go](https://github.com/qjfoidnh/BaiduPCS-Go)

**状态** · ❌ 已否决或不再跟踪 · ⭕ 待处理 · ⛔ 与 .NET 移植无关 · ✅ 已采纳或处理

## 拉取请求

| 拉取请求 | 作者 | 状态 | BaiduPCS.NET 处理结果 |
| --- | --- | :---: | --- |
| [#551](https://github.com/qjfoidnh/BaiduPCS-Go/pull/551) 优先使用 IPv6 | Jackson2z | ✅ | 已移植为 `network-family=prefer-ipv6`，可回退到 IPv4。IPv6 不可用的网络可设置 `network-family=ipv4`。地址顺序已用模拟测试验证。 |
| [#550](https://github.com/qjfoidnh/BaiduPCS-Go/pull/550) 只读分享文件清单 | victorwoo | ✅ | 已实现 `share inspect`。它分页遍历分享目录并返回 JSON 清单，不调用转存接口。公开分享的模拟测试覆盖了请求流程。 |
| [#537](https://github.com/qjfoidnh/BaiduPCS-Go/pull/537) 无需提取码的公开分享 | victorwoo | ✅ | 分享打开逻辑已支持。仅在显式提供提取码或链接中包含提取码时发起校验。 |
| [#532](https://github.com/qjfoidnh/BaiduPCS-Go/pull/532) 只读挂载 | xjackzenvey | ❌ | 不采纳只读文件系统挂载提案。文件操作通过 CLI 和强类型核心 API 提供。 |
| [#520](https://github.com/qjfoidnh/BaiduPCS-Go/pull/520) 自动拆分转存和 VLC 播放 | makeit2008 | ⭕ | 跟踪使用 VLC 播放当前帐号已有权限读取的网盘文件。不采纳靠拆分批次规避百度转存限制的做法。 [BaiduPCS.NET #11](https://github.com/ajtn123/BaiduPCS.NET/issues/11) |
| [#497](https://github.com/qjfoidnh/BaiduPCS-Go/pull/497) 实时传输进度表 | suntong | ✅ | 下载和上传采用实时表格。传输列宽固定，显示等待中、进行中和已完成的条目，终端高度不足时优先显示进行中的文件。 |
| [#403](https://github.com/qjfoidnh/BaiduPCS-Go/pull/403) 更新 `golang.org/x/crypto` | dependabot[bot] | ⛔ | Go 依赖维护。移植版使用 .NET 密码学 API，已通过已知答案和互操作测试。 |
| [#402](https://github.com/qjfoidnh/BaiduPCS-Go/pull/402) 更新 `golang.org/x/sys` | dependabot[bot] | ⛔ | Go 依赖维护。移植版直接使用 .NET 文件系统和网络 API。 |
| [#392](https://github.com/qjfoidnh/BaiduPCS-Go/pull/392) 可配置保存路径前缀 | phanen | ✅ | 已移植为 `download-prefix`，可使用共享相对目录或省略帐号前缀。使用 `@account` 可恢复默认布局。 |
| [#380](https://github.com/qjfoidnh/BaiduPCS-Go/pull/380) Bash 菜单 | dave2nian | ❌ | 不采纳独立 Bash 菜单。现有交互式终端已提供上下文帮助及命令和网盘路径补全。 |
| [#376](https://github.com/qjfoidnh/BaiduPCS-Go/pull/376) 更新 Go tablewriter | dependabot[bot] | ⛔ | Go 依赖维护。移植版以 Spectre.Console 渲染表格。 |
| [#375](https://github.com/qjfoidnh/BaiduPCS-Go/pull/375) 更新 `urfave/cli` | dependabot[bot] | ⛔ | Go 依赖维护。移植版使用 Spectre.Console.Cli 解析命令并显示帮助。 |
| [#374](https://github.com/qjfoidnh/BaiduPCS-Go/pull/374) 更新 Go runewidth | dependabot[bot] | ⛔ | Go 依赖维护。移植版使用 Spectre.Console 测量终端显示宽度。 |
| [#320](https://github.com/qjfoidnh/BaiduPCS-Go/pull/320) Gin HTTP API | wuzhican | ❌ | 不采纳 HTTP API。需要程序化访问时可使用强类型核心 API。 |
| [#213](https://github.com/qjfoidnh/BaiduPCS-Go/pull/213) Android cgo DNS 支持 | rhjdvsgsgks | ⛔ | 这是 Go/Termux 的打包修复，超出 .NET 桌面和服务器发布范围。 |
| [#205](https://github.com/qjfoidnh/BaiduPCS-Go/pull/205) 忽略符号链接遍历错误 | James4Ever0 | ✅ | 移植版跟踪正常链接，跳过失效链接和循环，并报告其他访问错误。 |

## 议题

待处理条目已根据上游原文、已有讨论和移植版行为重新核对。此状态用于尚未完成的移植功能，或仍能提供有效信息的具体对照检查。

后续验证使用了有效百度会话和可丢弃数据、本地故障模拟、独立核心 API 项目以及 GNU Screen。各条目注明测试所覆盖的帐号及环境，并标明仍需受影响会话或设备验证的情况。

| 议题 | 报告人 | 状态 | BaiduPCS.NET 处理结果 |
| --- | --- | :---: | --- |
| [#549](https://github.com/qjfoidnh/BaiduPCS-Go/issues/549) | SimpleRickSanchez | ❌ | 不采纳无法解释的周期性限速报告。滚动速率显示已验证，报告未给出可复现的客户端故障或请求差异。 |
| [#548](https://github.com/qjfoidnh/BaiduPCS-Go/issues/548) | huige007 | ❌ | 不采纳旧版多接口超时报告。实网容量查询、根目录列表、目录创建及元信息读取均通过，讨论未定位具体失败请求或修复方向。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/548#issuecomment-6104594765) |
| [#547](https://github.com/qjfoidnh/BaiduPCS-Go/issues/547) | ycc123abc | ✅ | `download --stream-discovery` 会在发现文件时开始传输。本地模拟测试验证了子目录尚未列完时即可下载已发现的文件。 |
| [#546](https://github.com/qjfoidnh/BaiduPCS-Go/issues/546) | ycc123abc | ❌ | 不采纳仅含截图的偶发 STOKEN 和重复错误报告。Cookie 解析已支持带引号的值和标点，报告尚无可复现原因。 |
| [#545](https://github.com/qjfoidnh/BaiduPCS-Go/issues/545) | duduXu | ✅ | 分享列表已处理。过期记录跳过提取码查询，单项查询失败不会影响其余条目。 |
| [#544](https://github.com/qjfoidnh/BaiduPCS-Go/issues/544) | netsonicyxf | ✅ | `list --folder-sizes` 递归统计并排序。模拟测试验证了嵌套文件会计入正确的一级子目录。 |
| [#542](https://github.com/qjfoidnh/BaiduPCS-Go/issues/542) | 2TempUser | ✅ | 上传目标查询识别两种表示路径缺失的错误码，因此可向新目标上传，包括新目录树中的文件。 |
| [#541](https://github.com/qjfoidnh/BaiduPCS-Go/issues/541) | qaz1qazlol2 | ⛔ | 杀毒软件告警涉及 Go 可执行文件。.NET 版发布自身的冒烟测试产物和 SHA-256 校验值。 |
| [#540](https://github.com/qjfoidnh/BaiduPCS-Go/issues/540) | zhongll19 | ✅ | 实网测试中已进入临时百度目录并读取元信息。模拟测试验证了对象缺失的返回方式，以及将截断的元信息报告为无效数据。未复现此前的 EOF。 |
| [#533](https://github.com/qjfoidnh/BaiduPCS-Go/issues/533) | jonathanzliu | ✅ | 已支持公开分享，只在提供或链接中包含提取码时进行校验。 |
| [#530](https://github.com/qjfoidnh/BaiduPCS-Go/issues/530) | lkfdev | ✅ | Linux musl 构建在 CI 的对应 .NET Alpine 容器中运行。讨论也确认了 Go 可通过容器部署。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/530#issuecomment-5314106659) |
| [#529](https://github.com/qjfoidnh/BaiduPCS-Go/issues/529) | LondyGhost | ✅ | 已实现。可设置 `network-family=ipv4` 避开不可达的 IPv6 路由。 |
| [#528](https://github.com/qjfoidnh/BaiduPCS-Go/issues/528) | XNN19 | ✅ | 所建议的 `266719` 已是 PCS 默认应用 ID，鉴权问题和传输不完整会分别报告。报告人未确认效果。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/528#issuecomment-4861310744) |
| [#527](https://github.com/qjfoidnh/BaiduPCS-Go/issues/527) | Xxx91n | ✅ | 命令参考、结构化列表输出和文档化的退出码已覆盖此需求。独立代理技能暂缓。 |
| [#525](https://github.com/qjfoidnh/BaiduPCS-Go/issues/525) | LFGN-L | ⛔ | OpenWrt 崩溃涉及 Go 可执行文件，该系统不在 .NET 发布矩阵内。 |
| [#522](https://github.com/qjfoidnh/BaiduPCS-Go/issues/522) | JerryRen471 | ✅ | 下载时使用同一张终端表显示等待、进行中及已完成项，优先保持正在下载的文件可见。 |
| [#518](https://github.com/qjfoidnh/BaiduPCS-Go/issues/518) | TwilightSunShine | ❌ | 资料不足。报告中的引号命令不完整，缺少完整示例及错误输出。分享导入支持单一链接和完整分享文本。 |
| [#517](https://github.com/qjfoidnh/BaiduPCS-Go/issues/517) | jacobin | ✅ | 成功、操作失败、输入错误和中断已使用不同的退出码。 |
| [#516](https://github.com/qjfoidnh/BaiduPCS-Go/issues/516) | rfvgyhnji | ✅ | 同名跳过会在读取本地文件以计算校验值或分片前返回。 |
| [#515](https://github.com/qjfoidnh/BaiduPCS-Go/issues/515) | Ajingsgh | ✅ | 可用 `--verify` 在写入最终目标前计算校验值，模拟测试覆盖内容不一致。 |
| [#514](https://github.com/qjfoidnh/BaiduPCS-Go/issues/514) | VashStan | ✅ | 目录迭代器支持超过 1,000 项的分页，递归下载也适用。 |
| [#513](https://github.com/qjfoidnh/BaiduPCS-Go/issues/513) | Goatofmountain | ✅ | 报告人确认 PCS 模式可避开直链错误 31023。PCS 与 Stream 两种模式均可显式选择，并与 Go 模拟测试一致。 [报告人确认](https://github.com/qjfoidnh/BaiduPCS-Go/issues/513#issuecomment-4562919308) |
| [#512](https://github.com/qjfoidnh/BaiduPCS-Go/issues/512) | JouSF-1409 | ✅ | 网关返回 HTML 时报告协议错误。帐号登录与 Pan 文件操作可能需要不同的百度主机。 |
| [#510](https://github.com/qjfoidnh/BaiduPCS-Go/issues/510) | cmcpp | ✅ | 讨论确认浏览器会话 Cookie 中包含 Pan STOKEN。可传入完整 Cookie 或 STOKEN，分片上传也可跳过秒传查询。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/510#issuecomment-4257123822) |
| [#508](https://github.com/qjfoidnh/BaiduPCS-Go/issues/508) | RuohanXiao | ❌ | 与 [#303](https://github.com/qjfoidnh/BaiduPCS-Go/issues/303) 的分享元信息鉴权报告重复。本帖只补充了错误出现的情况。 |
| [#506](https://github.com/qjfoidnh/BaiduPCS-Go/issues/506) | linhong88 | ✅ | 讨论定位了旧版 PCS 列目录条数过大的问题，并确认上游已修复。移植版限制页大小并测试了分页和回退。 [上游确认](https://github.com/qjfoidnh/BaiduPCS-Go/issues/506#issuecomment-4149722218) |
| [#505](https://github.com/qjfoidnh/BaiduPCS-Go/issues/505) | Gerry2439 | ✅ | 讨论建议跳过秒传查询。分片上传的 `--skip-rapid-check` 已通过模拟测试，同时仍会报告会话过期。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/505#issuecomment-4130690189) |
| [#502](https://github.com/qjfoidnh/BaiduPCS-Go/issues/502) | ctt08066-maker | ✅ | 可通过递归列目录和显式 `--output-directory` 处理路径结构。如果服务端仍漏列文件，需要实网对照。 |
| [#500](https://github.com/qjfoidnh/BaiduPCS-Go/issues/500) | Lisonwu6 | ✅ | 采用已完成分片检查点及有界重试。会话被判定过期时允许重建一次。 |
| [#499](https://github.com/qjfoidnh/BaiduPCS-Go/issues/499) | jlwang123456 | ❌ | 资料不足。只有分享网页报错，缺少脱敏响应或公开用例，无法区分链接过期、会话失败和接口回归。 |
| [#498](https://github.com/qjfoidnh/BaiduPCS-Go/issues/498) | huangshilai | ❌ | 资料不足。缺少生成的提取码、预期值、命令或响应。移植版可提交并显示用户提供的提取码。 |
| [#496](https://github.com/qjfoidnh/BaiduPCS-Go/issues/496) | zjc-2905130890 | ⛔ | 讨论说明 31365 表示百度文件大小限制，不同帐号阈值不同。客户端会显示服务端错误码。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/496#issuecomment-4764037633) |
| [#495](https://github.com/qjfoidnh/BaiduPCS-Go/issues/495) | suntong | ✅ | 实时表在空间允许时同时显示排队、进行中和已完成文件。重定向时保留逐行输出。 |
| [#494](https://github.com/qjfoidnh/BaiduPCS-Go/issues/494) | suntong | ✅ | 下载范围支持检查点，重复执行可恢复兼容的部分文件。 |
| [#493](https://github.com/qjfoidnh/BaiduPCS-Go/issues/493) | suntong | ✅ | 已提供 `--verify`、`--skip-verification` 和默认设置 `verify-downloads`。 |
| [#492](https://github.com/qjfoidnh/BaiduPCS-Go/issues/492) | ZhangLeiMing1802 | ❌ | 不采纳旧版 PCS 令牌错误报告。模拟测试保留百度错误 31030，登录支持浏览器 Cookie 及其令牌。 |
| [#491](https://github.com/qjfoidnh/BaiduPCS-Go/issues/491) | bobo-link | ❌ | 与 [#496](https://github.com/qjfoidnh/BaiduPCS-Go/issues/496) 的百度限制重复。请求失败仍会显示为提交失败。 |
| [#489](https://github.com/qjfoidnh/BaiduPCS-Go/issues/489) | trendnet-tennis | ❌ | 不采纳超出百度上传接口支持大小的改动。 |
| [#488](https://github.com/qjfoidnh/BaiduPCS-Go/issues/488) | Nihilismer | ✅ | 安装文档已说明如何运行单文件程序并加入 PATH，命令为 `baidupcs`。 |
| [#487](https://github.com/qjfoidnh/BaiduPCS-Go/issues/487) | bruse-zhang | ⛔ | 分享转存额度由帐号决定。移植版可按文件 ID 选择，并报告百度返回的额度信息。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/487#issuecomment-3765390139) |
| [#486](https://github.com/qjfoidnh/BaiduPCS-Go/issues/486) | JennieZhang1999 | ⭕ | 模拟测试确认 31066 表示找不到对象，新上传文件的大小也已实网核对。仍需提供网页可见但 CLI 拒绝读取的文件路径。 [BaiduPCS.NET #1](https://github.com/ajtn123/BaiduPCS.NET/issues/1) |
| [#485](https://github.com/qjfoidnh/BaiduPCS-Go/issues/485) | lxr-1204 | ❌ | 与 [#496](https://github.com/qjfoidnh/BaiduPCS-Go/issues/496) 的百度限制重复。客户端报告提交失败，并保留已确认分片。 |
| [#484](https://github.com/qjfoidnh/BaiduPCS-Go/issues/484) | 362227 | ✅ | 已实现 PCS 上传服务器发现，并允许固定服务器。报告未提供替代接口规范或可复现的发现故障。 |
| [#483](https://github.com/qjfoidnh/BaiduPCS-Go/issues/483) | chenhuping | ⭕ | 暂缓企业空间切换。需要独立的服务发现，并使用同时具有两种空间的帐号验证。 [BaiduPCS.NET #10](https://github.com/ajtn123/BaiduPCS.NET/issues/10) |
| [#482](https://github.com/qjfoidnh/BaiduPCS-Go/issues/482) | superdaa | ✅ | 报告人发现 Go 在大文件上传时仍会查询 UK。模拟测试覆盖 `--skip-rapid-check` 跳过该查询后发起分片预创建。 [报告人补充](https://github.com/qjfoidnh/BaiduPCS-Go/issues/482#issuecomment-3679907254) |
| [#481](https://github.com/qjfoidnh/BaiduPCS-Go/issues/481) | AllIGot2020 | ❌ | 不采纳仅有截图的上传提交失败报告。实网小文件分片提交和失败模拟测试均已通过，暂无可复现原因。 |
| [#480](https://github.com/qjfoidnh/BaiduPCS-Go/issues/480) | wcjjzz | ❌ | 元信息故障由 [#303](https://github.com/qjfoidnh/BaiduPCS-Go/issues/303) 跟踪。报告中另一条命令将分享链接传给了网盘列目录命令。 |
| [#479](https://github.com/qjfoidnh/BaiduPCS-Go/issues/479) | Lacacy | ✅ | 所链接的 bypy 做法跳过秒传和分片。实网测试中，Go 4.0.1 仅跳过秒传探测即可上传，.NET 使用 `--skip-rapid-check` 的分片上传也成功。 [bypy 相关报告](https://github.com/houtianze/bypy/issues/741) |
| [#477](https://github.com/qjfoidnh/BaiduPCS-Go/issues/477) | fgdsfgfdsgdsfgsdfg | ❌ | 合并至 [#449](https://github.com/qjfoidnh/BaiduPCS-Go/issues/449)，后者包含维护者对同类文件缺失现象和多种服务端表现的调查。 |
| [#476](https://github.com/qjfoidnh/BaiduPCS-Go/issues/476) | renmu123 | ✅ | 已支持 `upload --as NAME` 指定单文件目标名，校验和上传提交都使用该名称。 |
| [#475](https://github.com/qjfoidnh/BaiduPCS-Go/issues/475) | CaoMeiYouRen | ✅ | 可选的上传文件名限制已允许逗号。 |
| [#473](https://github.com/qjfoidnh/BaiduPCS-Go/issues/473) | ctt08066-maker | ⭕ | 实网并行上传生成两个名称不同且大小正确的远程条目。回读超时，尚无法确认两份文件逐字节一致。 [BaiduPCS.NET #2](https://github.com/ajtn123/BaiduPCS.NET/issues/2) |
| [#471](https://github.com/qjfoidnh/BaiduPCS-Go/issues/471) | nicole-coco | ✅ | 可用引号包住网盘通配符。按名称筛选嵌套目录内容的独立选项暂缓。 |
| [#469](https://github.com/qjfoidnh/BaiduPCS-Go/issues/469) | ai30328348 | ❌ | 不采纳规避百度下载带宽限制的改动，常规下载仍可使用现有传输模式及诊断功能。 |
| [#468](https://github.com/qjfoidnh/BaiduPCS-Go/issues/468) | ai30328348 | ✅ | 结果写入标准输出，错误和详细进度写入标准错误。可通过重定向保存后台任务的两路输出。 |
| [#467](https://github.com/qjfoidnh/BaiduPCS-Go/issues/467) | ZEntrosein | ✅ | 同一目录树内逐文件应用上传冲突策略。分享转存的合并方式由百度决定。 |
| [#465](https://github.com/qjfoidnh/BaiduPCS-Go/issues/465) | Alotofwater | ✅ | 上传失败时写入标准错误并返回失败退出码。 |
| [#464](https://github.com/qjfoidnh/BaiduPCS-Go/issues/464) | leewensong | ✅ | 讨论确认正确的会话令牌或来自其他帐号的分享可用于转存。支持 Cookie 和令牌输入，同帐号转存失败已记录为百度服务限制。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/464#issuecomment-3675973826) · [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/464#issuecomment-3757872130) |
| [#463](https://github.com/qjfoidnh/BaiduPCS-Go/issues/463) | Eaglex129 | ✅ | 同一会话内重试会保留已完成分片。服务端判定会话过期时可重新开始一次。 |
| [#460](https://github.com/qjfoidnh/BaiduPCS-Go/issues/460) | lcdyk0517 | ✅ | UK 查询失败时可回退到分片预创建，也可以显式跳过查询。上传仍会报告网络故障。 |
| [#458](https://github.com/qjfoidnh/BaiduPCS-Go/issues/458) | EvthPst | ✅ | 维护者确认依赖网络路由。移植版支持按主机配置代理，只调整 Pan 请求路由也可以，但传输速度仍取决于服务与网络。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/458#issuecomment-3411075263) |
| [#456](https://github.com/qjfoidnh/BaiduPCS-Go/issues/456) | yuyemin | ✅ | 维护者的手动帐号方案对应 `manual-account-name` 和显式 UID。帐号查询失败时可保存标识，实际操作仍需要有效凭据。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/456#issuecomment-3369003195) |
| [#455](https://github.com/qjfoidnh/BaiduPCS-Go/issues/455) | summersohot | ✅ | 可选择在写入最终目标前验证下载。若服务端 MD5 不可信，可对照已知的独立校验值。 |
| [#451](https://github.com/qjfoidnh/BaiduPCS-Go/issues/451) | 897601689 | ❌ | 不采纳规避服务端上传限速的改动。常规传输可配置速率和并发。 |
| [#449](https://github.com/qjfoidnh/BaiduPCS-Go/issues/449) | 362227 | ⭕ | 实网小批次分片上传重新列出时文件大小正确。仍可在报告成功前，通过现有元信息或列目录接口确认刚提交的文件。当前上传在删除本地源文件前已检查元信息。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/449#issuecomment-3457362169) · [BaiduPCS.NET #5](https://github.com/ajtn123/BaiduPCS.NET/issues/5) |
| [#447](https://github.com/qjfoidnh/BaiduPCS-Go/issues/447) | lianglunping | ✅ | 已处理会话过期，提交时收到 31061 可重新进行一次分片上传。 |
| [#446](https://github.com/qjfoidnh/BaiduPCS-Go/issues/446) | gzm2015 | ✅ | 重定向时默认不显示进度，已完成的分片会保留检查点。会话过期时可重新开始。 |
| [#443](https://github.com/qjfoidnh/BaiduPCS-Go/issues/443) | 362227 | ❌ | 大文件故障合并至 [#339](https://github.com/qjfoidnh/BaiduPCS-Go/issues/339)，上传成功后文件缺失的后续报告合并至 [#449](https://github.com/qjfoidnh/BaiduPCS-Go/issues/449)。 |
| [#441](https://github.com/qjfoidnh/BaiduPCS-Go/issues/441) | bu-code | ❌ | 不采纳将搜索和修改合成一个命令。上游建议先审查搜索结果，再交给单独的文件命令。`search --json` 可输出结构化结果。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/441#issuecomment-3245588295) |
| [#437](https://github.com/qjfoidnh/BaiduPCS-Go/issues/437) | sanwecn | ✅ | 等效选项为 `upload --skip-rapid-check`。 |
| [#428](https://github.com/qjfoidnh/BaiduPCS-Go/issues/428) | SeeFlowerX | ✅ | 数字形式的 `/share/link?shareid=…&uk=…` 通过分享和所有者 ID 列表及导入。实网检查报告中的链接，返回了全部 13 个文件和一个目录。 |
| [#423](https://github.com/qjfoidnh/BaiduPCS-Go/issues/423) | Qmumu | ⭕ | 暂缓服务端解压。需要单独的解压接口和符合条件的帐号。 [BaiduPCS.NET #9](https://github.com/ajtn123/BaiduPCS.NET/issues/9) |
| [#420](https://github.com/qjfoidnh/BaiduPCS-Go/issues/420) | Jason-2048 | ❌ | 合并至 [#544](https://github.com/qjfoidnh/BaiduPCS-Go/issues/544)。直接子项合计已可查看，递归目录大小需要单独遍历。 |
| [#418](https://github.com/qjfoidnh/BaiduPCS-Go/issues/418) | bugskill | ❌ | 不采纳旧版登录和列表问题。当前帐号的 Cookie 登录、容量查询、列目录和切换目录均已通过，讨论未定位可修复的故障。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/418#issuecomment-2994147267) |
| [#416](https://github.com/qjfoidnh/BaiduPCS-Go/issues/416) | 444026042 | ✅ | 可用操作系统的任务管理器启动独立 CLI，并将输出到标准错误的详细进度重定向到日志。 |
| [#414](https://github.com/qjfoidnh/BaiduPCS-Go/issues/414) | bladeofgod | ✅ | Cookie 解析支持外层引号、值中的等号、Base64 字符、百分号转义和逗号分隔值。模拟测试覆盖了触发 Go 警告的请求头格式。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/414#issuecomment-3339777405) |
| [#411](https://github.com/qjfoidnh/BaiduPCS-Go/issues/411) | bladeofgod | ✅ | Windows CLI 在命令提示符和 PowerShell 中均用引号包住含空格及 `&` 的路径，实网下载同一文件，两份输出的 SHA-256 均与源一致。 |
| [#410](https://github.com/qjfoidnh/BaiduPCS-Go/issues/410) | aa827508 | ✅ | 报告作者指出 STOKEN 与分享网页上的 bdstoken 被混用。移植版将 STOKEN 作为帐号凭据，另行解析网页令牌用于分享请求。 |
| [#408](https://github.com/qjfoidnh/BaiduPCS-Go/issues/408) | asasdsaweasd | ❌ | 只有截图，缺少能与 [#371](https://github.com/qjfoidnh/BaiduPCS-Go/issues/371) 区分的请求信息。移植版按分享条目隔离提取码查询失败。 |
| [#405](https://github.com/qjfoidnh/BaiduPCS-Go/issues/405) | huangshilai | ✅ | 当前帐号可通过标准输入将 Cookie 导入新配置。模拟测试也确认，帐号标识查询超时后可尝试备用接口。未复现其他帐号的故障。 |
| [#404](https://github.com/qjfoidnh/BaiduPCS-Go/issues/404) | sylingd | ✅ | 分片上传采用流式传输，等待响应头的时限独立于总传输时长。显式调整分片大小暂缓。 |
| [#399](https://github.com/qjfoidnh/BaiduPCS-Go/issues/399) | WolfLC | ✅ | 讨论指向已记录的 STOKEN 区别。移植版分别处理已保存的 STOKEN、分享页 bdstoken 和提取码。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/399#issuecomment-2869676669) |
| [#397](https://github.com/qjfoidnh/BaiduPCS-Go/issues/397) | wahahaer | ❌ | 合并至 [#544](https://github.com/qjfoidnh/BaiduPCS-Go/issues/544)，后者明确请求统计子目录的递归大小。 |
| [#396](https://github.com/qjfoidnh/BaiduPCS-Go/issues/396) | 384581169 | ✅ | 用 `share inspect` 找到文件 ID，再用 `share import --file-id` 选择指定条目。 |
| [#391](https://github.com/qjfoidnh/BaiduPCS-Go/issues/391) | KNaiFen | ✅ | `upload --remove-source` 删除本地源文件前验证远程大小及 MD5，包括因同名而跳过的文件。本地模拟测试确认校验不符时保留源文件。 |
| [#390](https://github.com/qjfoidnh/BaiduPCS-Go/issues/390) | WolfLC | ✅ | 架构文档标注 HTTP 传输层的位置，贡献指南说明构建方法。代理设置可用于常规网络路由。 |
| [#385](https://github.com/qjfoidnh/BaiduPCS-Go/issues/385) | yqs112358 | ✅ | 列表、元信息和搜索均支持用 `--json` 输出完整结构化条目。 |
| [#384](https://github.com/qjfoidnh/BaiduPCS-Go/issues/384) | dave2nian | ✅ | 上传已支持 `--skip-rapid-check`，仍会计算分片上传所需的校验值。 |
| [#378](https://github.com/qjfoidnh/BaiduPCS-Go/issues/378) | code-yuan-dev | ✅ | 只读 `share inspect` 支持公开分享和带提取码的分享。 |
| [#371](https://github.com/qjfoidnh/BaiduPCS-Go/issues/371) | moerrrr | ✅ | 已通过带帐号的分享列表实网检查。模拟测试发现可选提取码查询超时会中断整个列表。现已保留其余条目，并继续响应显式取消。 |
| [#356](https://github.com/qjfoidnh/BaiduPCS-Go/issues/356) | hnqyljl | ✅ | 已支持 IPv6 连接失败时仅用 IPv4，默认自动选择路由。 |
| [#355](https://github.com/qjfoidnh/BaiduPCS-Go/issues/355) | narfa0215 | ❌ | 不采纳改写网盘校验元信息。移植版可以验证下载并从文件内容计算校验值。 |
| [#354](https://github.com/qjfoidnh/BaiduPCS-Go/issues/354) | guoxk | ✅ | PCS 请求使用已配置的应用 ID，默认 `266719`。上传及列目录的模拟测试覆盖该参数。 |
| [#353](https://github.com/qjfoidnh/BaiduPCS-Go/issues/353) | JH6588 | ❌ | 本移植版不采纳群组库。上游维护者已调查独立群组接口，并表示没有实现计划。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/353#issuecomment-2542192154) |
| [#352](https://github.com/qjfoidnh/BaiduPCS-Go/issues/352) | joe12801 | ✅ | 直链查询会显示链接模式和相关用户代理。外部下载工具仍需满足会话和有效期要求。 |
| [#351](https://github.com/qjfoidnh/BaiduPCS-Go/issues/351) | JackySu | ⛔ | 报告人将 Go 崩溃定位到下载时的空值。.NET 下载器在提交前检查范围、直链响应及字节数，尚无相应崩溃报告。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/351#issuecomment-2764886608) |
| [#350](https://github.com/qjfoidnh/BaiduPCS-Go/issues/350) | daxiaamo | ❌ | 不采纳上传集群因未知集群 ID 返回 `31299` 的报告。实网分片上传通过，报告未给出可复现的参数或路由修正。 |
| [#346](https://github.com/qjfoidnh/BaiduPCS-Go/issues/346) | ghost | ✅ | 模拟测试确认，截断的元信息会导致响应无效错误。传输模拟及小文件实网下载通过，未复现旧服务端响应。 |
| [#345](https://github.com/qjfoidnh/BaiduPCS-Go/issues/345) | bloomachine | ✅ | 仅当远程文件确实为空时，零字节下载才有效。路径或下载请求失败会产生错误和失败退出码。 |
| [#343](https://github.com/qjfoidnh/BaiduPCS-Go/issues/343) | asasdsaweasd | ✅ | 交互式上传使用实时表格，重定向时仅在显式请求详细模式后显示进度。 |
| [#342](https://github.com/qjfoidnh/BaiduPCS-Go/issues/342) | calyle | ❌ | 不采纳使用未公开文档预览链接的做法。讨论中存在范围较小和 HTTP 403 问题，客户端使用标准下载接口。 |
| [#339](https://github.com/qjfoidnh/BaiduPCS-Go/issues/339) | reidmex | ❌ | 不再单独跟踪旧版大文件提交故障。上游结果不一，暂无可复现的客户端改动。移植版提交失败时保留已确认分片。 [报告人跟进](https://github.com/qjfoidnh/BaiduPCS-Go/issues/339#issuecomment-2942594972) |
| [#336](https://github.com/qjfoidnh/BaiduPCS-Go/issues/336) | stefanb | ⛔ | 这是 Go 编译器链接兼容性问题，移植版依赖 .NET SDK 和运行时。 |
| [#335](https://github.com/qjfoidnh/BaiduPCS-Go/issues/335) | leeivan | ✅ | 兼容的检查点可续传。会话被拒绝时可按服务端错误码重建一次。 |
| [#333](https://github.com/qjfoidnh/BaiduPCS-Go/issues/333) | snchrome | ⛔ | 报告涉及独立网页前端，讨论确认不属于 Go 命令行项目。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/333#issuecomment-2133349271) |
| [#330](https://github.com/qjfoidnh/BaiduPCS-Go/issues/330) | jiangjiangflies | ✅ | `sync --mode upload` 会按选定比较字段预览并上传缺失或有变化的文件。 [BaiduPCS.NET #12](https://github.com/ajtn123/BaiduPCS.NET/issues/12) |
| [#329](https://github.com/qjfoidnh/BaiduPCS-Go/issues/329) | ititchuan | ❌ | rclone 后端超出 CLI 移植范围。讨论指向独立的 rclone 工作，本项目为脚本提供结构化列表。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/329#issuecomment-2074680521) |
| [#328](https://github.com/qjfoidnh/BaiduPCS-Go/issues/328) | zhxsxuan | ❌ | 旧版秒传转存行为已在百度侧改变。移植版仍支持独立的可选秒传探测，并可回退到分片上传。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/328#issuecomment-2561683205) |
| [#327](https://github.com/qjfoidnh/BaiduPCS-Go/issues/327) | 5780358 | ✅ | 当前网络及会话实网创建分享成功，只读检查返回预期文件，随后已取消测试分享。未测试报告中的境外网络。 |
| [#326](https://github.com/qjfoidnh/BaiduPCS-Go/issues/326) | 34Hz-com | ✅ | 已支持 `download-prefix=`，可直接使用保存目录。 |
| [#324](https://github.com/qjfoidnh/BaiduPCS-Go/issues/324) | 5780358 | ❌ | 按凭据过期排查处理。讨论提到修改密码后会话失效且有效期不一致，移植版支持刷新凭据。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/324#issuecomment-2041795480) |
| [#323](https://github.com/qjfoidnh/BaiduPCS-Go/issues/323) | gavinwang668 | ✅ | 讨论提供 Cookie 登录作为替代，并确认密码登录报告重复。移植版支持 Cookie 输入，旧版密码流程的错误保留原服务码。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/323#issuecomment-3640841355) |
| [#322](https://github.com/qjfoidnh/BaiduPCS-Go/issues/322) | cangyuegui | ✅ | 在发布的许可材料中保留原版权声明并补充移植署名。 |
| [#321](https://github.com/qjfoidnh/BaiduPCS-Go/issues/321) | daxiaamo | ⭕ | 错误 `31299` 明确提示分片请求过期，可据此模拟。中断及提交模拟测试通过，仍需确认刷新会话能否在保留已确认分片的情况下恢复。 [BaiduPCS.NET #3](https://github.com/ajtn123/BaiduPCS.NET/issues/3) |
| [#319](https://github.com/qjfoidnh/BaiduPCS-Go/issues/319) | StrollStars | ✅ | 用 `match` 展开网盘通配符，用 `export` 导出递归目录清单。搜索使用百度关键词接口。 |
| [#318](https://github.com/qjfoidnh/BaiduPCS-Go/issues/318) | proItheus | ✅ | `share import --download` 下载百度转存后返回的路径。服务端未返回路径时，下载请求报失败。 |
| [#316](https://github.com/qjfoidnh/BaiduPCS-Go/issues/316) | yayo | ✅ | 暂存文件使用简短的哈希文件名，与远程基本文件名无关。 |
| [#314](https://github.com/qjfoidnh/BaiduPCS-Go/issues/314) | starpickuper | ❌ | 上游维护者确认该旧版秒传功能已不可用。移植版记录此限制，仍可导出校验记录。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/314#issuecomment-1853953470) |
| [#313](https://github.com/qjfoidnh/BaiduPCS-Go/issues/313) | a9udn9u | ✅ | `download --no-preallocate` 会逐步写入范围暂存文件。模拟测试核对了分段失败和恢复时的文件长度。 |
| [#312](https://github.com/qjfoidnh/BaiduPCS-Go/issues/312) | nations-Xiong | ⭕ | 跟踪通过 CLI 发现并访问百度同步空间。文件系统挂载及依赖挂载的界面集成未采纳。 [BaiduPCS.NET #16](https://github.com/ajtn123/BaiduPCS.NET/issues/16) |
| [#311](https://github.com/qjfoidnh/BaiduPCS-Go/issues/311) | Aarchbishop | ✅ | `list` 汇总直属文件和目录的计数。任意目录的完整递归计数暂缓。 |
| [#310](https://github.com/qjfoidnh/BaiduPCS-Go/issues/310) | NNNNAI | ✅ | 上游建议检查工作线程数。可用 `download --parallel` 设置每文件线程数，并显式选择直链模式，实际吞吐量仍取决于帐号。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/310#issuecomment-1793744924) |
| [#309](https://github.com/qjfoidnh/BaiduPCS-Go/issues/309) | NNNNAI | ✅ | 上游确认应先转存分享。移植版提供 `share import --download`。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/309#issuecomment-1793745136) |
| [#308](https://github.com/qjfoidnh/BaiduPCS-Go/issues/308) | VJ852 | ✅ | Python 示例使用已有配置，通过 CLI 查询并下载指定文件。 [示例](../examples/download.py) · [BaiduPCS.NET #15](https://github.com/ajtn123/BaiduPCS.NET/issues/15) |
| [#307](https://github.com/qjfoidnh/BaiduPCS-Go/issues/307) | LoserForever88 | ❌ | 不采纳原因不明的秒传命中差异。同内容实网测试回退到分片并成功，报告未提供能影响结果的已知请求变更。 |
| [#306](https://github.com/qjfoidnh/BaiduPCS-Go/issues/306) | 362227 | ❌ | 不采纳原因不明的服务端提交错误 31352。模拟测试确认，可保留已确认分片并在下一次执行时只重试提交。 |
| [#303](https://github.com/qjfoidnh/BaiduPCS-Go/issues/303) | joe12801 | ✅ | 讨论将一种错误归因于缺少 Pan 会话 Cookie，另有使用者确认新版上游已修复。移植版可传入完整 Cookie，分享元信息与导入已通过模拟测试。跨帐号实网验证另行跟踪。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/303#issuecomment-4824296789) · [上游确认](https://github.com/qjfoidnh/BaiduPCS-Go/issues/303#issuecomment-6719955842) |
| [#300](https://github.com/qjfoidnh/BaiduPCS-Go/issues/300) | cathy-imu | ⭕ | 暂缓导出自动生成的播放字幕。需要独立的播放接口和符合条件的帐号。 [BaiduPCS.NET #8](https://github.com/ajtn123/BaiduPCS.NET/issues/8) |
| [#289](https://github.com/qjfoidnh/BaiduPCS-Go/issues/289) | turtoe | ✅ | 每文件进度条显示完成字节、一分钟平均速度和剩余时间。数字列按当前窗口使用固定宽度。 |
| [#288](https://github.com/qjfoidnh/BaiduPCS-Go/issues/288) | limitless-share | ❌ | 合并至 [#303](https://github.com/qjfoidnh/BaiduPCS-Go/issues/303)。本帖涉及偶发元信息错误和同帐号转存，暂无独立的移植版复现用例。 |
| [#287](https://github.com/qjfoidnh/BaiduPCS-Go/issues/287) | ChitaGideon | ⭕ | 跟踪校验链接导出，前提是百度分享接口能为有权读取的文件提供所需数据。 [BaiduPCS.NET #7](https://github.com/ajtn123/BaiduPCS.NET/issues/7) |
| [#286](https://github.com/qjfoidnh/BaiduPCS-Go/issues/286) | damoncui668 | ✅ | 旧版密码流程使用屏蔽输入和 HTTPS，诊断输出省略敏感响应。 |
| [#283](https://github.com/qjfoidnh/BaiduPCS-Go/issues/283) | yangfl | ⛔ | 报告人将旧版 Go 故障归因于内存分配器，暂无对应的移植版故障或当前协议记录。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/283#issuecomment-1627662252) |
| [#282](https://github.com/qjfoidnh/BaiduPCS-Go/issues/282) | 362227 | ✅ | 已支持 `share create --code CODE --days DAYS`。 |
| [#281](https://github.com/qjfoidnh/BaiduPCS-Go/issues/281) | jacklee789 | ✅ | 当前会话可检查新创建的短分享，同帐号转存返回服务错误 2。数字形式的 `/share/link` 由 [#428](https://github.com/qjfoidnh/BaiduPCS-Go/issues/428) 单独跟踪。 |
| [#280](https://github.com/qjfoidnh/BaiduPCS-Go/issues/280) | WQY916 | ❌ | 不采纳省略切片校验值的重放方式。移植版不重放旧秒传链接，导出记录会保留校验值。 |
| [#279](https://github.com/qjfoidnh/BaiduPCS-Go/issues/279) | bensjohn | ✅ | 已实现稳定版发现，按运行时精确选择并校验发布资产。仍需能连接发布服务。 |
| [#278](https://github.com/qjfoidnh/BaiduPCS-Go/issues/278) | bigmouse0001 | ✅ | 已支持用 `--out` 指定导出文件名。 |
| [#277](https://github.com/qjfoidnh/BaiduPCS-Go/issues/277) | linktomarkdown | ✅ | 一个独立 .NET 项目引用核心 API，脱离 CLI 构建并运行了校验值和路径测试。可以通过源码将客户端集成到其他服务。 |
| [#276](https://github.com/qjfoidnh/BaiduPCS-Go/issues/276) | yoyosbg | ❌ | 保留百度要求的删除授权，并报告服务端拒绝。 |
| [#275](https://github.com/qjfoidnh/BaiduPCS-Go/issues/275) | williamjieh | ❌ | 群组库请求合并至 [#353](https://github.com/qjfoidnh/BaiduPCS-Go/issues/353)。公开分享导入与独立的群组库服务分开。 |
| [#274](https://github.com/qjfoidnh/BaiduPCS-Go/issues/274) | 362227 | ❌ | 不采纳生成专有客户端缓存数据库。需要索引时可导出结构化清单。 |
| [#273](https://github.com/qjfoidnh/BaiduPCS-Go/issues/273) | doubilbm | ✅ | 分片检查点和有界重试减少进度反复重置。会话过期可重建一次。 |
| [#272](https://github.com/qjfoidnh/BaiduPCS-Go/issues/272) | ghost | ✅ | 配置、命令历史和上传检查点的位置已记录。下载检查点保存在暂存文件旁。 |
| [#271](https://github.com/qjfoidnh/BaiduPCS-Go/issues/271) | yayo | ✅ | `upload --as NAME` 可显式命名单个文件，目标目录仍通过位置参数指定。 |
| [#270](https://github.com/qjfoidnh/BaiduPCS-Go/issues/270) | yayo | ✅ | 已支持 `share import --destination PATH`。 |
| [#268](https://github.com/qjfoidnh/BaiduPCS-Go/issues/268) | yayo | ❌ | 分片上传按设定速率流式传输，未采纳独立的无分片模式。 |
| [#266](https://github.com/qjfoidnh/BaiduPCS-Go/issues/266) | daxiaamo | ✅ | `config set skip-rapid-check=true` 设置上传默认行为，`--rapid-check` 可在单次执行中覆盖。管道上传模拟已验证保存设置可跳过秒传请求。 |
| [#265](https://github.com/qjfoidnh/BaiduPCS-Go/issues/265) | newnames | ✅ | 上传会话过期后会失效，可重建一次。其他服务错误照常报告。 |
| [#264](https://github.com/qjfoidnh/BaiduPCS-Go/issues/264) | daiaji | ❌ | 不集成第三方下载加速器，现有直链模式与传输设置可供使用。 |
| [#263](https://github.com/qjfoidnh/BaiduPCS-Go/issues/263) | Raywoo1 | ✅ | 有界元信息请求会报告 TLS 超时。可使用 IPv4 选择、代理和本地绑定选项调整路由。 |
| [#262](https://github.com/qjfoidnh/BaiduPCS-Go/issues/262) | QGB | ✅ | 报告无法读取的文件，并继续处理其余已枚举文件。存在因访问错误而跳过的文件时，批次返回失败。 |
| [#261](https://github.com/qjfoidnh/BaiduPCS-Go/issues/261) | stark131 | ❌ | 不采纳持久化刷新间隔。实时输出原地刷新，重定向后可显式请求工作线程或详细日志。 |
| [#259](https://github.com/qjfoidnh/BaiduPCS-Go/issues/259) | 362227 | ✅ | `list --json` 和 `metadata --json` 可查看各文件大小。目录树中重复显示该值暂缓。 |
| [#257](https://github.com/qjfoidnh/BaiduPCS-Go/issues/257) | Left024 | ✅ | 已提供 `list --json` 用于脚本。 |
| [#256](https://github.com/qjfoidnh/BaiduPCS-Go/issues/256) | 362227 | ⭕ | 小型实网分片上传重新列出时保持原有非零大小。可通过现有元信息接口进行有界校验重试，以区分大小更新延迟与持续为空。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/256#issuecomment-2770602818) · [BaiduPCS.NET #4](https://github.com/ajtn123/BaiduPCS.NET/issues/4) |
| [#255](https://github.com/qjfoidnh/BaiduPCS-Go/issues/255) | SinoHan | ✅ | `sync --mode remove-missing` 预览指定目录内的单向镜像删除，并要求显式指定方向。 [BaiduPCS.NET #13](https://github.com/ajtn123/BaiduPCS.NET/issues/13) |
| [#254](https://github.com/qjfoidnh/BaiduPCS-Go/issues/254) | daiaji | ✅ | `share create --combined` 已支持在链接中附带提取码。剪贴板集成暂缓。 |
| [#253](https://github.com/qjfoidnh/BaiduPCS-Go/issues/253) | qwsxmkoi | ⛔ | 旧 ARMv7 打包不在发布矩阵内。受支持的 64 位 Linux 环境提供 Arm64 自包含版。 |
| [#251](https://github.com/qjfoidnh/BaiduPCS-Go/issues/251) | daiaji | ✅ | 元信息由百度提供，可能与实际文件内容不同。本地 `checksum` 根据文件字节计算。 |
| [#250](https://github.com/qjfoidnh/BaiduPCS-Go/issues/250) | TensorABC | ✅ | 导出会遍历目录并取得所需校验值。提速或调整并发需要先确认可复现的瓶颈。 |
| [#249](https://github.com/qjfoidnh/BaiduPCS-Go/issues/249) | wtwangcb | ✅ | 已记录旧版秒传重放命令不可用。校验值齐全时仍可导出互操作的链接格式。 |
| [#247](https://github.com/qjfoidnh/BaiduPCS-Go/issues/247) | daiaji | ✅ | 匹配器支持在单个路径分量内用 `!(pattern|pattern)` 排除模式。保留 `*`、`?` 以及方括号字面量行为。 |
| [#242](https://github.com/qjfoidnh/BaiduPCS-Go/issues/242) | arstum | ❌ | 按订阅功能位置的使用问题处理，报告未给出可实现的 CLI 行为或服务接口。 |
| [#240](https://github.com/qjfoidnh/BaiduPCS-Go/issues/240) | proItheus | ✅ | `download --resume-all` 读取当前帐号已保存的目标和选项。模拟测试中断范围下载，再启动新进程续传，未重复下载完成的范围。 |
| [#238](https://github.com/qjfoidnh/BaiduPCS-Go/issues/238) | beiank | ✅ | `--verbose` 提供诊断。使用已认证的旧版配置导入或屏蔽输入可传入现有凭据。 |
| [#237](https://github.com/qjfoidnh/BaiduPCS-Go/issues/237) | hzhangxyz | ✅ | 保留上游对空文件的处理方式。空远程对象仍需单独实网验证。 |
| [#236](https://github.com/qjfoidnh/BaiduPCS-Go/issues/236) | hzhangxyz | ✅ | 遍历会跟踪有效链接，并跳过失效链接和循环。 |
| [#233](https://github.com/qjfoidnh/BaiduPCS-Go/issues/233) | cainslayer | ⛔ | 这是 Termux 上的 Go 运行时错误，移植版发布矩阵覆盖桌面及受支持的 Linux 运行时。 |
| [#232](https://github.com/qjfoidnh/BaiduPCS-Go/issues/232) | lihanchen | ⭕ | 在当前帐号上重复上传会跳过两个已存在文件，目录中每个名称只出现一次。仍未复现约 6,500 个文件的规模。 [BaiduPCS.NET #6](https://github.com/ajtn123/BaiduPCS.NET/issues/6) |
| [#231](https://github.com/qjfoidnh/BaiduPCS-Go/issues/231) | joe12801 | ❌ | 资料不足，缺少命令、错误信息和脱敏分享。具体元信息问题由 [#303](https://github.com/qjfoidnh/BaiduPCS-Go/issues/303) 跟踪。 |
| [#230](https://github.com/qjfoidnh/BaiduPCS-Go/issues/230) | joe12801 | ✅ | `rename --pattern` 和 `--replacement` 对所选远程文件名做正则替换。执行前检查目标，可用 `--dry-run` 预览。 |
| [#229](https://github.com/qjfoidnh/BaiduPCS-Go/issues/229) | youlanying | ❌ | 移植版不采纳原生二维码登录，可导入由浏览器登录获得的 Cookie。 |
| [#228](https://github.com/qjfoidnh/BaiduPCS-Go/issues/228) | lee850220 | ✅ | `share list` 已支持指定页。单次获取全部页和逐条提取码暂缓。 |
| [#227](https://github.com/qjfoidnh/BaiduPCS-Go/issues/227) | Heclgg | ❌ | 不继续调查 Orange Pi 吞吐量。Linux Arm64 原生包已通过发布冒烟测试。 |
| [#226](https://github.com/qjfoidnh/BaiduPCS-Go/issues/226) | qunlan | ❌ | 不采用行号作为选择器，因为它会随排序和过滤变化。结构化输出提供稳定路径和文件 ID。 |
| [#225](https://github.com/qjfoidnh/BaiduPCS-Go/issues/225) | ownerowner | ✅ | 作者确认本地文件完整，损坏的是终端输出。移植版把文件字节写入目标，终端进度只显示状态和计数。 [报告人说明](https://github.com/qjfoidnh/BaiduPCS-Go/issues/225#issuecomment-1236176926) |
| [#224](https://github.com/qjfoidnh/BaiduPCS-Go/issues/224) | 123485k | ✅ | 已分别提供下载与上传带宽限制。 |
| [#219](https://github.com/qjfoidnh/BaiduPCS-Go/issues/219) | Mw-HbCK | ✅ | 已支持 `upload --parallel`、`--file-concurrency` 及相应的配置默认值。 |
| [#218](https://github.com/qjfoidnh/BaiduPCS-Go/issues/218) | newyue588cc | ❌ | 不继续调查密码和验证码流程。支持浏览器 Cookie 及其中的令牌，帐号标识模拟测试验证了收到 HTML 响应后的回退。 |
| [#216](https://github.com/qjfoidnh/BaiduPCS-Go/issues/216) | Amarillys | ❌ | 资料不足，只有截图，未提供可复现的 Shell、命令序列或错误。CI 覆盖常规 Windows 控制台执行。 |
| [#215](https://github.com/qjfoidnh/BaiduPCS-Go/issues/215) | haobinnan | ❌ | 不采纳批量播放已停用的旧版秒传链接。维护者在 [#314](https://github.com/qjfoidnh/BaiduPCS-Go/issues/314) 确认了这一限制，校验导出仍保留链接数据。 |
| [#214](https://github.com/qjfoidnh/BaiduPCS-Go/issues/214) | yangmeishu | ❌ | 不采纳 BaiduPCS.NET 的网页管理界面。项目提供命令行操作。 |
| [#212](https://github.com/qjfoidnh/BaiduPCS-Go/issues/212) | denniszzg | ✅ | 下载默认跳过已有目标文件，使用 `--overwrite` 可覆盖。 |
| [#210](https://github.com/qjfoidnh/BaiduPCS-Go/issues/210) | rsj123 | ⛔ | 报告中的崩溃发生于 Go 的 DNS 查询。.NET 使用系统解析并报告连接失败。 |
| [#206](https://github.com/qjfoidnh/BaiduPCS-Go/issues/206) | Myriad-Dreamin | ✅ | `BaiduPCS.NET.Core` 可供应用程序单独引用，无需启动 CLI。独立发布 SDK 不在此项范围内。 |
| [#204](https://github.com/qjfoidnh/BaiduPCS-Go/issues/204) | hangcidu | ✅ | 已发布的 Linux x64 程序在 Arch Linux WSL 的 GNU Screen 5.0.3 分离式伪终端中执行 `pwd` 和 `exit`，并成功退出。 |
| [#203](https://github.com/qjfoidnh/BaiduPCS-Go/issues/203) | hulu204 | ❌ | 将重复的停顿现象合并至 [#549](https://github.com/qjfoidnh/BaiduPCS-Go/issues/549)。旧帖仅提供速度截图，缺少同一文件请求对照。 |
| [#199](https://github.com/qjfoidnh/BaiduPCS-Go/issues/199) | kdurov | ❌ | 发布保留含版本的文件名，以供校验及更新器发现。用 `update check` 查看稳定版。 |
| [#198](https://github.com/qjfoidnh/BaiduPCS-Go/issues/198) | shenwii | ✅ | `upload - REMOTE_FILE` 暂存二进制标准输入以计算大小和校验值。模拟测试确认分片请求中的字节与输入完全一致。 |
| [#197](https://github.com/qjfoidnh/BaiduPCS-Go/issues/197) | zzb0 | ❌ | 资料不足，只有截图，缺少命令、版本、错误文字和后续说明。 |
| [#195](https://github.com/qjfoidnh/BaiduPCS-Go/issues/195) | mlsub | ✅ | 两种发布方式均提供原生 Windows Arm64 单文件构建，发布流程在 Windows Arm 运行器中执行验证。 |
| [#194](https://github.com/qjfoidnh/BaiduPCS-Go/issues/194) | albb762 | ✅ | Go 程序可将 `baidupcs` 作为子进程运行，并使用其退出码和 JSON 输出。 |
| [#192](https://github.com/qjfoidnh/BaiduPCS-Go/issues/192) | linhandev | ✅ | 每个文件都有已传输字节数和总大小。多个文件并发时位置编号含义不明，因此暂缓任务位置计数。 |
| [#191](https://github.com/qjfoidnh/BaiduPCS-Go/issues/191) | zzb0 | ✅ | 上游维护者指出路径中的空格需要引号，之后又有人报告非 ASCII 名称。CLI 支持带引号路径，协议模拟覆盖带 Unicode 及 Shell 特殊字符的文件名。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/191#issuecomment-1080142042) |
| [#188](https://github.com/qjfoidnh/BaiduPCS-Go/issues/188) | Pete-Young | ❌ | 不再调查旧版挂载目标上的定位错误。缺少可复现的文件系统情形，跟进仅涉及路径引号用法。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/188#issuecomment-1073148181) |
| [#187](https://github.com/qjfoidnh/BaiduPCS-Go/issues/187) | boyodyang | ✅ | 可分别设置 PCS、Pan 和默认请求的用户代理。 |
| [#184](https://github.com/qjfoidnh/BaiduPCS-Go/issues/184) | boyodyang | ⛔ | 堆栈止于 Go DNS 解析器。移植版使用 .NET 网络栈并报告连接失败。 [报告人的堆栈跟踪](https://github.com/qjfoidnh/BaiduPCS-Go/issues/184#issuecomment-1068070951) |
| [#182](https://github.com/qjfoidnh/BaiduPCS-Go/issues/182) | waiav123 | ⛔ | 报告人确认百度网页端也出现同一参数错误。CLI 校验本地分享选项，并报告服务端鉴权及参数错误。 [报告人确认](https://github.com/qjfoidnh/BaiduPCS-Go/issues/182#issuecomment-1061354978) |
| [#181](https://github.com/qjfoidnh/BaiduPCS-Go/issues/181) | 123485k | ✅ | 目录同步可预览上传、下载及仅处理缺失路径的变更，支持递归、通配符和试运行。 [BaiduPCS.NET #14](https://github.com/ajtn123/BaiduPCS.NET/issues/14) |
| [#179](https://github.com/qjfoidnh/BaiduPCS-Go/issues/179) | moox2020 | ❌ | 合并至 [#353](https://github.com/qjfoidnh/BaiduPCS-Go/issues/353)。上游指出群组使用独立的解析及鉴权，暂无实现计划。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/179#issuecomment-1053218857) |
| [#174](https://github.com/qjfoidnh/BaiduPCS-Go/issues/174) | hanksuper | ✅ | 失败时会报告路径、错误及最终退出码。脚本输出需要专用格式时再考虑按组汇总。 |
| [#173](https://github.com/qjfoidnh/BaiduPCS-Go/issues/173) | jyuan15 | ❌ | 不再跟踪上游记录的 3.8.5 历史回归。目前显示为零字节的文件由 [#256](https://github.com/qjfoidnh/BaiduPCS-Go/issues/256) 跟踪。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/173#issuecomment-1027705944) |
| [#172](https://github.com/qjfoidnh/BaiduPCS-Go/issues/172) | Jieoz | ❌ | 不采纳规避百度客户端限制的改动。报告文件已通过 Direct、PCS 和 Stream 的有限读取测试，模拟测试覆盖 HTTP 403 与百度错误 31326 的报告。 |
| [#170](https://github.com/qjfoidnh/BaiduPCS-Go/issues/170) | mir355 | ❌ | 不内置忽略文件筛选。上游建议在终端中先筛选，本项目可接收显式选定的本地路径。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/170#issuecomment-1027831596) |
| [#163](https://github.com/qjfoidnh/BaiduPCS-Go/issues/163) | Niuyongchao | ❌ | 讨论确认升级并刷新配置后旧版 Go 恢复正常，暂无相应移植版报告。 [上游确认](https://github.com/qjfoidnh/BaiduPCS-Go/issues/163#issuecomment-1010529460) |
| [#159](https://github.com/qjfoidnh/BaiduPCS-Go/issues/159) | sonataaaa | ✅ | 一次下载支持多个路径和通配符。独立的持久队列暂缓。 |
| [#158](https://github.com/qjfoidnh/BaiduPCS-Go/issues/158) | jiangjiangflies | ✅ | 可按配置启用文件名预检查，检查模式允许逗号。 |
| [#148](https://github.com/qjfoidnh/BaiduPCS-Go/issues/148) | Naivedevil | ✅ | 上游建议调整上传并发。移植版提供每文件线程数、文件并发数和速率限制，实际速度取决于网络与服务。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/148#issuecomment-1001950291) |
| [#147](https://github.com/qjfoidnh/BaiduPCS-Go/issues/147) | Jieoz | ✅ | 服务端拒绝范围请求时回退到顺序下载。有限次数重试后，工作线程故障作为传输失败报告。 |
| [#144](https://github.com/qjfoidnh/BaiduPCS-Go/issues/144) | jimbos | ✅ | 上传可通过 `overwrite-if-size-differs` 按文件大小增量覆盖。 |
| [#141](https://github.com/qjfoidnh/BaiduPCS-Go/issues/141) | wtwangcb | ✅ | 传输默认保留修改时间。用 `--no-preserve-mtime` 关闭。 |
| [#139](https://github.com/qjfoidnh/BaiduPCS-Go/issues/139) | Yarke99 | ✅ | 可查看分享清单、按文件 ID 导入并在转存后下载。第三方分享直接下载链接暂缓。 |
| [#138](https://github.com/qjfoidnh/BaiduPCS-Go/issues/138) | ghost | ⛔ | 这是 Go 的同步运行时崩溃。.NET 上传调度和检查点序列化使用独立的运行时组件。 |
| [#130](https://github.com/qjfoidnh/BaiduPCS-Go/issues/130) | maxadc | ✅ | 模拟测试复现了贴吧请求超时阻断 Pan 备用查询。修复后会继续尝试备用标识查询，并响应显式取消。当前网络的 Cookie 登录也已通过。 |
| [#107](https://github.com/qjfoidnh/BaiduPCS-Go/issues/107) | zhiting9420 | ⛔ | Termux 解析器配置不在发布矩阵内。受支持的平台使用系统 DNS。 |
| [#92](https://github.com/qjfoidnh/BaiduPCS-Go/issues/92) | pongfcnkl | ❌ | 在获得等效 Windows 复现环境前，受影响帐号已经恢复。Direct 模式 HTTP 403 的可执行对照由 [#172](https://github.com/qjfoidnh/BaiduPCS-Go/issues/172) 跟踪。 [报告人跟进](https://github.com/qjfoidnh/BaiduPCS-Go/issues/92#issuecomment-877543599) |
| [#90](https://github.com/qjfoidnh/BaiduPCS-Go/issues/90) | zhaobinGitHub | ✅ | 限制目录页大小并检测停滞页面，测试覆盖超过 1,000 项的列表。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/90#issuecomment-877520872) |
| [#89](https://github.com/qjfoidnh/BaiduPCS-Go/issues/89) | boyodyang | ✅ | 下载整棵目录树前会扫描路径冲突。大型目标也可拆成多次命令。 |
| [#87](https://github.com/qjfoidnh/BaiduPCS-Go/issues/87) | GeorgeJoestar | ⛔ | Android 的 DNS 和热点不在发布矩阵内。受支持的客户端可配置代理、本地绑定和地址族。 |
| [#76](https://github.com/qjfoidnh/BaiduPCS-Go/issues/76) | nazhendenb | ❌ | 报告人确认换网后登录和转存正常，维护者未能复现 Windows 故障。 [报告人跟进](https://github.com/qjfoidnh/BaiduPCS-Go/issues/76#issuecomment-850922328) · [维护者跟进](https://github.com/qjfoidnh/BaiduPCS-Go/issues/76#issuecomment-850926225) |
| [#2](https://github.com/qjfoidnh/BaiduPCS-Go/issues/2) | ws4liu | ⛔ | 维护者确认这是帐号鉴权限制，讨论未提供有效的客户端修复。移植版显示百度返回的鉴权错误码。 [上游讨论](https://github.com/qjfoidnh/BaiduPCS-Go/issues/2#issuecomment-729649742) |
