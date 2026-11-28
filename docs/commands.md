# 命令参考

此表列出 BaiduPCS-Go 基线 `1b91318` 和 BaiduPCS.NET 的命令对应关系。迁移脚本时，请采用 BaiduPCS.NET 栏中的命令及相应选项。 [上游基线](https://github.com/qjfoidnh/BaiduPCS-Go/tree/1b9131817aaf8ca7dee24bc00e33ebc4c7a5cc73)

| BaiduPCS-Go | BaiduPCS.NET | 说明 |
| --- | --- | --- |
| `login` | `account login` | 用 `--cookies` 或 `--cookies-stdin` 登录，也可用 `--bduss` 或 `--bduss-stdin` 传入浏览器 Cookie 中的 BDUSS。BDUSS 登录可选 `--ptoken` 和 `--stoken`。密码及其验证码登录不受支持。 |
| `loglist`、`who`、`su`、`logout` | `account list`、`account current`、`account switch`、`account logout` | 按 UID、名称或交互式菜单切换帐号。脚本用 `logout --yes` 确认移除帐号。 |
| `setastoken` | `account set-access-token` | 在屏蔽输入的提示框中输入令牌，或使用 `--stdin`。 |
| `quota` | `quota` | 查看总容量、已用空间和剩余空间。 |
| `cd`、`pwd` | `cd`、`pwd` | `cd --list` 可同时列出目标目录。 |
| `ls`、`l`、`ll` | `list` | 别名为 `ls` 和 `ll`。支持 `--totals`、`--sort name\|time\|size`、`--descending`、`--folder-sizes` 和 `--json`。 |
| `search`、`s` | `search` | 支持 `--path`、`--recursive`、`--totals` 和 `--json`。 |
| `tree`、`t`、`meta`、`match` | `tree`、`metadata`、`match` | `metadata` 的别名为 `meta`。`tree --depth` 限制目录层数，`--file-ids` 显示 ID。`metadata --json` 返回完整路径和文件 ID。 |
| `mkdir`、`rm`、`cp`、`mv` | `mkdir`、`remove`、`copy`、`move`、`rename` | 支持别名 `rm`、`cp` 和 `mv`。复制和移动可以指定多个源路径和一个目标。重命名支持 `--pattern`、`--replacement` 和 `--dry-run`。 |
| `d`、`download` | `download` | 别名为 `d`。`--resume-all` 恢复当前帐号的下载队列，`--stream-discovery` 在遍历时开始传输，`--no-preallocate` 按进度写入文件。运行 `download --help` 查看选项。 |
| `lt`、`locate` | `download-links` | 别名为 `locate`。使用 `--pan` 调用网盘网页接口。 |
| `u`、`upload` | `upload` | 别名为 `u`。指定本地路径或通配符及网盘目录，也可用 `upload - REMOTE_FILE` 上传二进制标准输入。`--as NAME` 为单个文件指定目标名称，`--remove-source` 在校验后删除本地源文件。 |
| `sf`、`sumfile` | `checksum` | 输出大小、完整 MD5、前 256 KiB 的 MD5、CRC32 和校验链接。 |
| `ep`、`export` | `export` | `--stdout` 仅输出记录，`--out` 将记录追加到文件。支持 `--format manifest\|checksum-link`、`--root`、`--recursive` 和 `--retries`。 |
| `share set`、`share list`、`share cancel` | `share create`、`share list`、`share cancel` | 支持 `--code`、`--days`、`--combined` 和 `list --page`。 |
| `transfer` | `share import` | 接受短链接、旧版数字形式的 `/share/link` 链接或复制的分享文本。支持 `--destination PATH`、`--code`、`--file-id`、`--collect` 和 `--download`。 |
| 上游提案 | `share inspect` | 以 JSON 输出分享清单，包括大小合计及目录列表是否完整。 |
| `offlinedl`、`od`、`clouddl` | `offline add\|query\|list\|cancel\|delete` | 添加时可用 `--path`，列表可用 `--page`，删除时可指定 ID 或 `--all`。 |
| `recycle` | `recycle list\|restore\|delete` | 列表支持 `list --page`。永久删除可指定 ID 或 `--all`。 |
| `config`、`config set`、`config reset`、`env` | `config show`、`config set KEY=VALUE...`、`config reset`、`env` | `config show` 列出支持的短横线式设置名称。可用 `BAIDUPCS_CONFIG_DIR` 和 `BAIDUPCS_VERBOSE` 环境变量。 |
| `run`、`clear`、`cls`、`quit`、`exit` | `run -- EXECUTABLE ARG...`、`clear`、`exit` | 支持别名 `cls` 和 `quit`。`shell` 显式启动交互式终端。 |
| `tool showtime`、`getip`、`enc`、`dec` | `tool time`、`tool ip`、`tool encrypt`、`tool decrypt` | 加解密支持 `--method`、`--key-stdin`、`--no-gzip`、`--remove-source` 和 `--overwrite`。 |
| `update` | `update check`、`update apply` | `apply --yes` 跳过确认。更新器校验对应运行时的程序并保留 `.previous` 备份。 |

`account`、`offline`、`recycle` 和 `share` 的 `list` 都可以简写为 `ls`。`offline` 和 `recycle` 的 `delete` 可以简写为 `rm`。

## 安装

PowerShell 安装脚本默认为 Windows 的 `%LOCALAPPDATA%\BaiduPCS.NET\bin` 或 macOS、Linux 的 `~/.local/bin`。Shell 安装脚本默认使用 `~/.local/bin`，需要 `curl`、`jq` 或 Python 3、以及 `sha256sum` 或 `shasum`。两个脚本均支持通过 `BAIDUPCS_INSTALL_DIR` 指定目录，也可分别传入 `-InstallDir` 或 `--install-dir`。需强制使用自包含版时，指定 `-SelfContained` 或 `--self-contained`。 [PowerShell 脚本](../install.ps1) · [Shell 脚本](../install.sh)

手动下载时，根据系统和架构选择资产。带有 `-framework-dependent` 的版本需要 .NET 10 或更新的兼容运行时，自包含版随程序附带运行时。使用发布页上的 SHA-256 核对下载文件。 [最新发布](https://github.com/ajtn123/BaiduPCS.NET/releases/latest)

| 系统 | 资产后缀 |
| --- | --- |
| Windows | `win-x64.exe`、`win-arm64.exe` |
| macOS | `osx-x64`、`osx-arm64` |
| Linux（glibc） | `linux-x64`、`linux-arm64` |
| Linux（musl） | `linux-musl-x64`、`linux-musl-arm64` |

Windows 可将文件重命名为 `baidupcs.exe`，再用 PowerShell 执行 `.\baidupcs.exe --help`。macOS 和 Linux 可将文件重命名为 `baidupcs`，执行 `chmod +x baidupcs`，再用 `./baidupcs --help` 查看帮助。可以将程序所在目录加入 PATH。

Windows 上会在当前进程退出后由辅助程序完成更新，下次启动时清理已完成的更新辅助文件。要回滚，请关闭程序，并将 `.previous` 恢复为原可执行文件名。

## 帐号与配置

`account import-go` 读取现有 Go 配置，写入独立的 BaiduPCS.NET 配置。便携安装可以传入 `pcs_config.json` 的路径。`baidupcs env` 显示配置文件位置，设置 `BAIDUPCS_CONFIG_DIR` 可使用独立配置。配置文件含有会话凭据，请保存在仅本人可访问的目录中。

`account login --cookies-stdin` 接受浏览器复制的一条 Cookie 请求头。解析时支持可选的 `Cookie:` 前缀、带引号和逗号分隔的值，以及现有百分号转义。会话令牌和已编码字符串等 Cookie 值会原样传送。交互式终端支持 `help`、Tab 补全和 `exit`。相对网盘路径以当前帐号的工作目录为基准。通配符可用引号包住，交由 BaiduPCS.NET 展开。

## 传输示例

```sh
baidupcs list /reports --sort size --descending --totals
baidupcs download '/reports/*.csv' --output-directory ./reports --verify
baidupcs upload './reports/*.csv' /backup --conflict overwrite-if-size-differs
baidupcs export /backup --recursive --root /restored --stdout
```

默认使用直链下载。如果当前帐号或地区无法使用，可尝试 `--mode pcs` 或 `--mode stream`。`--link-index` 从 0 开始。`--test` 读取下载数据而不写入本地文件。传输始终校验长度，并在可用时按元信息校验值验证。目标文件已存在时默认跳过，可用 `--overwrite` 覆盖。

上传和下载默认保留文件修改时间。上传向百度发送本地修改时间，下载优先使用远程文件的客户端时间，缺失时使用服务端时间。使用 `--no-preserve-mtime` 可让目标端分配时间。管道上传无源文件时间。续传沿用队列中的传输选项。

交互式终端和一次性终端命令都会为下载、上传和同步显示实时表格。条目从上到下排列，进行中的文件各有进度条。终端高度不足时，优先显示进行中、即将开始、队列中较靠后以及已完成的条目。结果表包含所有已完成的操作和每个文件本次传输的用时。已传输、速度和时间列采用固定宽度，宽度不足时显示较少的列。「预计剩余 / 用时」列对进行中的文件显示预计剩余时间，对传输已结束的文件显示实际用时。速度取最近 60 秒平均值，新文件按已传输时长计算，剩余时间据此估算。`--worker-status` 显示下载工作线程的进度。重定向后逐行输出。`--raw-numbers` 按字节和秒显示数值，支持 `--json` 的命令可输出机器可读结果。

列目录默认使用分页的 Pan 接口。会话被拒绝时，可回退到 BaiduPCS-Go 4.0.1 所用的 PCS 接口。PCS 达到 1,000 项上限时，需要使用 Pan 会话 Cookie 登录才能获取完整目录。

## 目录同步

两个根目录都必须存在。`-r` 或 `--recursive` 包含子目录。命令先显示计划并请求确认，`--dry-run` 仅显示计划，`--yes` 跳过确认，重定向输入或输出时需要显式提供该选项。

| 模式 | 操作 |
| --- | --- |
| `upload` | 将本地新增或有变化的文件上传到网盘。 |
| `download` | 将网盘新增或有变化的文件下载到本地。 |
| `transfer-missing` | 默认双向补齐缺失路径。用 `--direction upload` 或 `--direction download` 指定单向补齐。 |
| `remove-missing` | 删除目标端存在而源端缺失的文件。向网盘删除用 `--direction upload`，向本地删除用 `--direction download`。启用递归且未指定通配符时，还会删除检查时已为空的目标端独有目录。 |

只处理缺失项的模式按相对路径比较。上传和下载默认使用 `--compare name,time,length`，也可用逗号组合 `name`、`time`、`length`、`checksum`。启用 `name` 时路径名称必须精确匹配，省略时忽略大小写，并报告名称冲突。时间精确到秒。下载优先保留远程文件的客户端修改时间，缺失时使用服务端时间。上传将本地修改时间发送给百度。校验值比较使用完整文件 MD5。

可重复指定 `--glob`，文件匹配任一模式即可。`*.csv` 等文件名模式匹配任意扫描深度，`reports/**/*.csv` 等路径模式以两个根目录为基准，使用 `/` 分隔。过滤器只匹配文件，删除计划会保留未匹配的目录。目录列表不完整或计划中的路径发生变化时，同步会停止。失败或中断后可重新预览剩余操作。

```sh
baidupcs sync ./reports /reports --mode upload -r --glob '*.csv' --dry-run
baidupcs sync ./reports /reports --mode download -r --compare length,checksum
baidupcs sync ./reports /reports --mode transfer-missing -r
baidupcs sync ./reports /reports --mode remove-missing --direction upload -r --dry-run
```

## Python 自动化

先完成一次登录，再向示例脚本传入网盘文件的绝对路径和本地输出目录。脚本复用已保存的配置，以 JSON 读取文件元信息，已有同名本地文件时跳过。程序不在 PATH 中时指定 `--baidupcs`。 [Python 示例](../examples/download.py)

```sh
python examples/download.py '/reports/Quarter 1.csv' ./reports
```

## 批量文件操作

```sh
baidupcs list /reports --folder-sizes
baidupcs match '/reports/!(draft*|*.tmp)'
baidupcs rename --pattern '^draft-(.+)\.txt$' --replacement 'final-$1.txt' --dry-run '/reports/draft-*.txt'
```

`--folder-sizes` 遍历目录树，并按累计文件大小排列直接子目录。`!(draft*|*.tmp)` 这样的否定通配符会在单个路径分量中排除匹配任一备选模式的项。批量重命名对每个文件名执行正则替换，变更前检查源路径、目标唯一性和目标占用情况。用 `--dry-run` 预览源路径和目标。

## 传输队列与上传选项

```sh
baidupcs download /archive --stream-discovery --output-directory ./archive
baidupcs download --resume-all
baidupcs download /huge.tar --no-preallocate
baidupcs config set skip-rapid-check=true
baidupcs upload ./notes.txt /backup --rapid-check
cat ./archive.tar | baidupcs upload - /backup/archive.tar
baidupcs upload ./verified-copy.tar /backup --remove-source
```

配置会在传输前记录已发现的文件，并在完成或跳过后移出队列。`--resume-all` 使用已保存的目标及选项恢复当前帐号的任务。常规下载在传输前规划整个目录树。使用 `--stream-discovery` 时，后续路径冲突被发现前，部分文件可能已下载。重新执行发现命令可以补齐尚未发现的路径。`--no-preallocate` 让单个文件按顺序增量写入，多个文件仍可并发下载。

`skip-rapid-check=true` 跳过后续上传的秒传探测，`--rapid-check` 可为单次上传启用探测。管道上传将二进制输入暂存为临时文件，以便计算大小和校验值。管道应使用一次性 CLI。如果要跨进程续传，先把管道输出保存为本地文件，再上传该文件。

`--remove-source` 仅在百度返回的元信息同时匹配文件大小和 MD5 时删除本地源文件。即使远程已存在且上传被跳过，也执行相同检查。验证失败会保留文件，处理源文件删除时会跳过树中的链接。

默认保存目录下有一个以当前帐号命名的子目录。设置 `download-prefix=` 可直接使用已配置的保存目录，`download-prefix=shared` 使用共享子目录，`download-prefix=@account` 恢复按帐号分目录。`network-family=prefer-ipv6` 优先尝试 IPv6，失败后回退到 IPv4。`network-family=ipv4` 限制为 IPv4。

导出清单为逐行 JSON。文件记录包含 `type`、`path`、`length`、`md5`、`sliceMd5` 和 `crc32`，空目录对应 `directory` 记录。`--format checksum-link` 输出与 Go 兼容的 `MD5#sliceMD5#length#name` 文本。Go 命令中停用的秒传链接导入和随机重命名开关目前无法用于还原。

## 旧版加密文件

`aes-128`、`aes-192` 和 `aes-256` 与 `ctr`、`cfb` 和 `ofb` 组合成九种模式，均采用旧版带 IV 前缀的格式。默认开启压缩。解密时需使用相同的模式、密钥和 gzip 设置。旧格式未对密文做身份验证。`--remove-source` 在输出写入成功后执行。使用 `--no-gzip` 时，格式本身无法判定密钥是否正确。

Windows 上与 BaiduPCS-Go 4.0.1 的双向文件对照验证了九种无压缩模式。Go 的默认 gzip 流程报告临时文件重命名错误，中间的压缩和解压字节与 .NET 兼容。
