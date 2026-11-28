# 架构与 API

## 项目

| 项目 | 职责 |
| --- | --- |
| `BaiduPCS.NET.Cli` | 注册、解析和校验命令，提供交互提示、命令行编辑与补全，显示传输状态和错误 |
| `BaiduPCS.NET.Core` | 管理配置、百度请求、远程路径、传输、校验值、旧版文件加密和更新暂存 |
| `BaiduPCS.NET.Tests` | 模拟协议请求和响应，测试传输中断与故障、密码学向量、归档校验、参数解析和本地化 |

每个受支持的运行时都提供自包含和依赖框架的单文件可执行程序。两者都包含 `BaiduPCS.NET.Core`，解决方案内可调用其强类型 API。

## 命令执行

一次性执行和交互式终端都把相同的参数数组交给 Spectre 命令应用。设置类型会在请求服务前解析选项并校验本地输入。交互式终端使用同一份命令注册信息实现补全，并通过当前帐号查询网盘路径。直接包含密码、密钥、Cookie、访问令牌或提取码的命令不会写入历史记录。

命令通过标准输出返回数据和结果。在终端中，传输和批量操作会显示实时进度表及结果表。重定向时改为逐行输出，错误和显式请求的进度写入标准错误。退出码 `0` 表示成功，`1` 表示单项操作或批次失败，`2` 表示输入无效，`130` 表示中断。`run` 传递子进程的退出码。首次中断会停止调度并保留已完成的分段或分片记录，再次中断会立即退出。

## 核心 API

| API | 用途 |
| --- | --- |
| `ProfileStore.ReadAsync`、`UpdateAsync` | 读取配置，在锁保护下原子更新设置或帐号状态 |
| `LegacyProfile.ReadAsync`、`Merge` | 只读解析 Go 配置，按 UID 合并帐号并导入设置 |
| `PassportLogin.SignInAsync`、`IPassportPrompts` | 处理旧版密码及验证流程。BaiduPCS.NET 登录命令无需此流程 |
| `AccountIdentity.ResolveAsync` | 根据会话解析帐号，支持已配置的手动帐号名称 |
| `NetdiskClient` | 使用 `RemoteEntry` 列目录、搜索和读取元信息，查询容量，管理文件、直链、传输、分享、离线任务和回收站 |
| `FileChecksums.ComputeAsync`、`GetExportChecksumsAsync` | 计算本地校验值，或通过元信息和范围请求读取远程导出所需的校验值 |
| `LegacyFileCrypto` | 以流式方式处理带 IV 前缀的旧版 AES 模式和 gzip 格式 |
| `ReleaseUpdater` | 发现 GitHub 稳定版，并校验、暂存和安装对应运行时的可执行程序 |

使用 `ClientSettings` 和目标帐号的会话创建 `NetdiskClient`，在操作或批次结束后释放。公共异步方法接受 `CancellationToken`。`BaiduApiException` 提供服务数字错误码和 HTTP 状态，消息中省略请求地址和凭据。

```csharp
using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;

var store = new ProfileStore(ProfileStore.DefaultDirectory());
Profile profile = await store.ReadAsync(cancellationToken);
using var client = new NetdiskClient(profile.Settings, profile.RequireAccount());

await foreach (RemoteEntry entry in client.ListAsync("/", cancellationToken: cancellationToken))
    Console.WriteLine(entry.Path);
```

## 状态、网络与传输

Windows 使用漫游应用数据目录，macOS 使用 `~/Library/Application Support`，Linux 使用 `XDG_CONFIG_HOME` 或 `~/.config`。`BAIDUPCS_CONFIG_DIR` 可覆盖默认位置。设置和帐号保存在 `profile.json` 中，更新时先加锁并将数据写入临时文件，刷新后再替换原文件。Unix 下配置文件及新建配置目录仅当前用户可访问。Windows 文件继承上级目录的权限。

每个客户端维护独立的 Cookie 容器，并根据接口设置请求格式和用户代理。传输层支持 HTTP/SOCKS5 代理和本地接口选择，并验证 HTTPS 证书。读取操作会重试临时网络及服务错误，永久删除等操作跳过自动重试。旧版 Passport 流程使用独立会话，并检查重定向目标。

下载先探测服务器的范围请求支持情况，然后将互不重叠的范围写入临时文件，并在目标目录的 `.baidupcs-partial` 下记录进度。服务端忽略范围请求时改为顺序下载。检查点按帐号、远程文件标识、大小和修改时间匹配。上传会在配置目录的 `uploads` 下保存会话和分片检查点，按帐号和目标匹配，并验证本地源文件。下载在校验后替换目标文件，上传按分片顺序提交校验值。Go 使用另一种检查点格式，已有任务可用 BaiduPCS-Go 完成。

传输结束并关闭锁时会移除对应的 `.lock`。下载暂存目录为空时也会自动移除。失败或取消后保存的临时文件和检查点可供下次续传。

## 验证

离线测试覆盖分页、PCS 列目录回退、路径匹配、远程变更错误、下载签名、链接过期、分段与分片恢复、验证挑战、归档校验和旧版 AES 向量。CI 用模拟配置比较一次性执行和交互式终端的行为。实网对照使用独立配置和可丢弃的文件。

与 BaiduPCS-Go 4.0.1 的实网对照已验证配置导入、容量查询、文件和目录操作、双向传输、创建和取消分享、回收站还原，以及离线任务的查询、列表和删除。

百度可能更改接口或按帐号限制功能。在测试会话中，两款客户端都无法生成网盘网页下载链接，也无法将分享转存回创建分享的帐号。直链下载成功。重复提交离线任务时，两者都收到错误 `36040`。Go 默认上传失败，加上 `--norapid` 后成功。.NET 的 `--skip-rapid-check` 分片上传也成功。清空帐号级记录经模拟测试验证，从另一帐号转存分享仍待实网验证。

固定基线中的 Go 命令已关闭独立的秒传链接导入。校验链接和清单导出仍可用于记录文件元信息。
