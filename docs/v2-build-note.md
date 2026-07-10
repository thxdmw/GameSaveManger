# V2 构建说明

V2 目标框架为 .NET 10，界面框架为 WPF，只支持 Windows 客户端。

本地构建：

```powershell
dotnet restore .\GameSaveManager.V2.sln
dotnet build .\GameSaveManager.V2.sln -c Debug
```

运行前需要：

- Windows。
- .NET 10 SDK。
- 可访问 GameSave CMS 服务端。
- 服务端已执行 `docs/db/file_system.sql` 和 `docs/db/game_save.sql`。
- MinIO 中存在 `game-save-private` Bucket，或部署流程负责创建。

## 自动构建

`.github/workflows/v2-build.yml` 会在 V2 Solution 或四个 V2 项目发生变化时触发 Windows 构建：

```text
windows-latest
    ↓
.NET 10 SDK
    ↓
dotnet restore
    ↓
dotnet build -c Debug --no-restore
```

GitHub Actions 使用 GitHub 官方维护的 checkout/setup-dotnet Action。PR 在自动构建通过前保持 Draft，不直接合并。
