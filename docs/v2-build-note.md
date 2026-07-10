# V2 构建说明

V2 目标框架为 .NET 10，界面框架为 WPF，只支持 Windows 客户端。

本地构建：

```powershell
dotnet restore .\GameSaveManager.V2.sln
dotnet build .\GameSaveManager.V2.sln -c Debug
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

`.github/workflows/v2-build.yml` 会在 V2 Solution、正式 V2 项目、Verification 项目或工作流自身发生变化时触发 Windows 构建：

```text
windows-latest
    ↓
.NET 10 SDK
    ↓
dotnet restore
    ↓
dotnet build -c Debug --no-restore
    ↓
GameSaveManager.Verification
    ↓
上传 restore/build/verification 日志 artifact
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

## 注释规范

V2 新增 C# XML 注释和专用文档统一使用中文。代码标识符、协议字段和稳定错误码继续使用英文，避免影响代码可读性和接口兼容性。
