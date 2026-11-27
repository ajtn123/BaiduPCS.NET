# BaiduPCS.NET

[![构建状态](https://github.com/ajtn123/BaiduPCS.NET/actions/workflows/ci.yml/badge.svg?branch=main&event=push)](https://github.com/ajtn123/BaiduPCS.NET/actions/workflows/ci.yml)
[![最新版本](https://img.shields.io/github/v/release/ajtn123/BaiduPCS.NET)](https://github.com/ajtn123/BaiduPCS.NET/releases/latest)

基于 .NET 10 的百度网盘命令行客户端。支持交互式终端、批量传输和目录同步，源自 [BaiduPCS-Go](https://github.com/qjfoidnh/BaiduPCS-Go)。

## 安装

运行 Windows 的 [PowerShell 安装脚本](install.ps1) 或 macOS、Linux 的 [Shell 安装脚本](install.sh)。

```powershell
./install.ps1
```

```sh
sh ./install.sh
```

脚本根据系统、架构和已安装的 .NET 运行时选择版本，并校验下载文件的 SHA-256。Windows 默认安装到 `%LOCALAPPDATA%\BaiduPCS.NET\bin`，macOS 和 Linux 默认安装到 `~/.local/bin`。也可从发布页直接下载。 [最新发布](https://github.com/ajtn123/BaiduPCS.NET/releases/latest) · [手动安装与选项](docs/commands.md#安装)

## 快速开始

将安装目录加入 PATH。导入 BaiduPCS-Go 配置后即可列目录、上传和下载。

```sh
baidupcs account import-go
baidupcs ls /
baidupcs u ./notes.txt /
baidupcs d /notes.txt --output-directory ./downloads
```

也可用 `account login --cookies-stdin` 登录。直接运行 `baidupcs` 进入交互式终端，或用 `baidupcs --help` 查看命令。百度可能按帐号限制功能或停用接口。 [已验证功能与服务限制](docs/architecture.md#验证)

## 文档

[命令参考与目录同步](docs/commands.md) · [贡献指南](CONTRIBUTING.md) · [安全政策](SECURITY.md) · [发布指南](docs/release.md) · [第三方声明](THIRD-PARTY-NOTICES.md)

项目采用 [MIT 许可](LICENSE)。
