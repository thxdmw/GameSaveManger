# 构建说明

项目目标框架为 .NET 10，界面框架为 WPF，只支持 Windows 客户端。

本地构建：

```powershell
dotnet restore .\GameSaveManager.sln
dotnet build .\GameSaveManager.sln -c Debug
```

执行基础边界验证：

```powershell
dotnet run --project .\tests\GameSaveManager.Verification\GameSaveManager.Verification.csproj -c Debug --no-build
```

运行正式客户端前需要：

- Windows。
- .NET 10 SDK。
- 可访问 GameSave CMS 服务端。
- 远程 GameSave 服务端使用 HTTPS；HTTP 仅允许 localhost/回环地址。
- 服务端已执行 `docs/db/file_system.sql` 和 `docs/db/game_save.sql`。
- MinIO 中存在 `game-save-private` Bucket，或部署流程负责创建。

## 自动构建

`.github/workflows/build.yml` 会在 解决方案、正式项目、Verification 项目或工作流自身发生变化时触发 Windows 构建：

```text
windows-latest
    ↓
.NET 10 SDK
    ↓
dotnet restore
    ↓
dotnet build -c Debug / Release --no-restore
    ↓
dotnet test -c Release --no-build
    ↓
GameSaveManager.Verification
    ↓
生成两种 Windows 发布目录并校验第三方资源
    ↓
上传日志和发布包 artifact
```

当前 Verification 覆盖：

- localhost HTTP 允许。
- 远程 HTTP 拒绝。
- 服务端基础地址 Query/Fragment 拒绝。
- scheme/host 大小写规范化。
- 基础 path 大小写保持隔离。
- 旧 `sync_state` schema migration。
- `server_key + game_id` 同步 HEAD 隔离。

GitHub Actions 使用 GitHub 官方维护的 checkout/setup-dotnet/upload-artifact Action。PR 在自动构建和基础边界验证通过前保持 Draft，不直接合并。

推送与 `Directory.Build.props` 一致的 `v主版本.次版本.修订版本` 标签后，`.github/workflows/release.yml` 会执行同一套 Release 验证，使用与仓库公开证书一致的自签 PFX 调用 SignTool 和 Inno Setup，生成带可信时间戳的签名安装包，再生成覆盖除校验文件自身以外全部附件的 SHA-256 校验和及独立签名更新清单，并创建不可覆盖的 GitHub 预发布。详细步骤见 [版本管理与发布流程](versioning.md) 和 [发布签名与恢复手册](signing-and-recovery.md)。

## 注释规范

项目 C# XML 注释和专用文档统一使用中文。代码标识符、协议字段和稳定错误码继续使用英文，避免影响代码可读性和接口兼容性。
