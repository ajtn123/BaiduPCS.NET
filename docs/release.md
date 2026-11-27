# 发布指南

CI 会还原锁定的依赖并运行离线测试，再用模拟配置对比 Linux、macOS 和 Windows 上的一次性命令与交互式终端。八个运行时标识各在对应的 GitHub 托管运行器上构建并测试两种单文件可执行程序。自包含版启用压缩和非复合 ReadyToRun。musl 的依赖框架版在 .NET Alpine 运行时镜像中检查，自包含版使用运行时依赖镜像。安装脚本会用最新稳定版验证两种安装方式。 [托管运行器标签](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)

## 发布步骤

1. 与用户一起完成 BaiduPCS-Go 实网对照，记录服务限制并修复客户端回归。
2. 确认待发布提交的 CI 通过，再推送稳定版标签，例如 `v1.4.0`。工作流会重新构建标签对应的可执行程序，通过冒烟测试后发布到 GitHub。
3. 确认 CI 已将 GitHub 提供的 SHA-256 与 16 个可执行程序、两个安装脚本和 `LICENSES.txt` 逐一比较。运行 `baidupcs update check` 验证客户端可发现此版本。

自包含资产命名为 `BaiduPCS.NET-v<VERSION>-<runtime>`。依赖框架版增加 `-framework-dependent` 后缀，Windows 程序再以 `.exe` 结尾。依赖框架版优先使用 .NET 10。如未安装，将使用下一个兼容的 .NET 主版本。更新时会保留已安装的构建类型。 [运行时版本选择](https://learn.microsoft.com/zh-cn/dotnet/core/versions/selection)

更新器暂存程序前，会根据 GitHub 发布 API 提供的 SHA-256 校验所选资产。发布页的资产列表也显示该校验值。 [GitHub 发布资产 API](https://docs.github.com/en/rest/releases/assets)
