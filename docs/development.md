# 开发与构建

GameSave Manager 面向 Windows，目标框架为 .NET 10，界面使用 WPF。

## 开发环境

- Windows 10 或 Windows 11。
- .NET 10 SDK。
- 可访问的 GameSave CMS 服务端。
- 远程服务端必须使用 HTTPS；HTTP 仅允许 localhost 或其他回环地址。
- 已配置私有对象存储，默认 Bucket 为 `game-save-private`，也可以由部署流程负责创建。

## 本地构建

```powershell
dotnet restore .\GameSaveManager.sln
dotnet build .\GameSaveManager.sln -c Debug
```

执行基础边界验证：

```powershell
dotnet run --project .\tests\GameSaveManager.Verification\GameSaveManager.Verification.csproj -c Debug --no-build
```

执行完整 Release 构建与测试：

```powershell
dotnet restore .\GameSaveManager.sln
dotnet build .\GameSaveManager.sln -c Release --no-restore
dotnet test .\GameSaveManager.sln -c Release --no-build
dotnet run --project .\tests\GameSaveManager.Verification\GameSaveManager.Verification.csproj -c Release --no-build
```

## 本地发布

生成默认的 `win-x64` 自包含发布目录：

```powershell
.\scripts\publish-windows.ps1
```

生成依赖 .NET 10 Desktop Runtime 的调试分发目录：

```powershell
.\scripts\publish-windows.ps1 -DeploymentMode FrameworkDependent
```

安装 Inno Setup 6 后可以生成本地安装包：

```powershell
.\scripts\build-installer.ps1
```

发布目录和安装包输出位于 `artifacts/`。该目录以及根目录的 `publish/` 都是可重新生成的本地产物，不应提交到仓库。

安装范围为当前 Windows 用户，程序默认安装到 `%LOCALAPPDATA%\Programs\GameSaveManager`。SQLite、日志、内容缓存和更新事务保存在 `%LOCALAPPDATA%\GameSaveManager`，设备凭据保存在 Windows Credential Manager。

签名构建、版本标签和 GitHub Release 流程见 [发布流程](release.md)，证书和密钥管理见 [发布签名与恢复手册](signing-and-recovery.md)。

## 自动构建

`.github/workflows/build.yml` 在解决方案、项目、Verification 或工作流自身发生变化时执行以下流程：

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

GitHub Actions 使用 GitHub 官方维护的 `checkout`、`setup-dotnet` 和 `upload-artifact` Action。自动构建和基础边界验证通过前，不应合并变更。

## 自动验证基线

当前 Verification 覆盖：

- WPF 应用资源、主窗口和全部页面可在 STA 线程加载，绑定错误会使验证失败。
- 服务端 URL 安全规则、`serverKey` 规范化和跨服务端状态隔离。
- SQLite schema 迁移、旧配置兼容和关键唯一性约束。
- 协议契约、CMS 时间转换、请求重试、结构化日志和请求 ID。
- 游戏启动安全、进程识别、添加向导状态隔离和存档候选校验。
- 注册表预览、恢复 Journal 启动恢复和游戏详情同步状态。
- 客户端更新通道、SemVer、偏好持久化、签名清单、摘要链、Authenticode 发布者固定和失败回滚基础流程。

这些自动验证不能代替真实 CMS、对象存储、游戏进程、多设备冲突和 Windows 安装升级环境中的端到端验收。

## 注释规范

项目 C# XML 注释和专用文档统一使用中文。代码标识符、协议字段和稳定错误码继续使用英文，避免影响代码可读性和接口兼容性。
