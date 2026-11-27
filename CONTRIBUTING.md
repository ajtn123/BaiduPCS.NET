# 参与 BaiduPCS.NET 开发

安装支持 `net10.0` 的 .NET SDK，然后在仓库根目录构建并运行离线测试。

```sh
dotnet restore BaiduPCS.NET.slnx --locked-mode
dotnet build BaiduPCS.NET.slnx --no-restore
dotnet test --solution BaiduPCS.NET.slnx --no-build --no-restore
dotnet run --project src/BaiduPCS.NET.Cli -- --help
```

## 报告问题

先查看现有议题。报告时请附上 BaiduPCS.NET 版本、系统及架构、命令、预期结果和脱敏后的输出。若遇到百度服务错误，请提供数字错误码。如果已对照 BaiduPCS-Go 或官方客户端，也请说明同一操作的结果。 [议题列表](https://github.com/ajtn123/BaiduPCS.NET/issues)

公开报告中请勿附上配置文件、Cookie、令牌、分享链接或私有路径。疑似漏洞请通过私密渠道报告。 [安全政策](SECURITY.md)

## 提交变更

- 在拉取请求中说明行为变化和验证方式。
- 命令解析和显示放在 CLI 项目中，网盘请求和传输逻辑放在核心项目中。 [项目架构](docs/architecture.md)
- 协议测试使用模拟响应。实网测试使用独立的 `BAIDUPCS_CONFIG_DIR` 和可丢弃的网盘文件。
- 新增命令、选项或别名时更新命令参考。变更包引用时更新依赖锁文件。 [命令参考](docs/commands.md)
