# Windows 发布与安装

客户端支持两种 .NET 10 Windows 发布模式：

- `SelfContained`：默认模式，随安装包携带 .NET Runtime，目标电脑无需预装 Runtime。
- `FrameworkDependent`：体积更小，但目标电脑必须预装匹配的 .NET 10 Desktop Runtime。

默认架构是 `win-x64`；ARM Windows 可改用 `win-arm64`。

## 生成发布目录

在仓库根目录执行自包含发布：

```powershell
.\scripts\publish-windows.ps1 -Version 0.1.0
```

输出目录为 `artifacts\publish\win-x64`。生成依赖 Runtime 的发布物：

```powershell
.\scripts\publish-windows.ps1 -DeploymentMode FrameworkDependent -Version 0.1.0
```

输出目录为 `artifacts\publish\win-x64-framework-dependent`。发布脚本没有启用 trimming：WPF、SQLite 原生库和系统凭据调用对动态加载较敏感，先保证可用性和可诊断性，再根据真实发布物做专项裁剪验证。

## 生成安装包

先安装 Inno Setup 6，然后执行。脚本会把同一个版本同时写入客户端程序集和安装包：

```powershell
.\scripts\build-installer.ps1 -Version 0.1.0
```

安装包会输出到 `artifacts\installer`，脚本同时生成可随安装包发布的 `SHA256SUMS.txt`。安装范围为当前 Windows 用户，不需要管理员权限；程序文件位于 `%LOCALAPPDATA%\Programs\GameSaveManager`，游戏存档、SQLite、日志及凭据仍分别使用应用数据目录和 Windows Credential Manager。

如果 Inno Setup 不在默认目录，可显式提供编译器：

```powershell
.\scripts\build-installer.ps1 `
  -Version 0.1.0 `
  -InnoSetupCompiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

## 发布前检查

```powershell
dotnet build .\GameSaveManager.sln --no-restore
dotnet run --project .\tests\GameSaveManager.Verification\GameSaveManager.Verification.csproj --no-build
```

发布到真实服务端前还应运行 CMS 仓库的 Docker Compose 冒烟链路。远程服务端地址必须使用 HTTPS；客户端只允许 `localhost` 和 `127.0.0.1` 使用 HTTP。

## 签名与更新

安装包当前不内置签名证书、私钥或自动更新地址。正式对外分发前，应在受保护的 CI 凭据库中配置代码签名证书，并使用 `signtool` 对 EXE 与安装包签名、时间戳。自动更新也应由 HTTPS 下载源、已签名的版本清单和可回滚策略组成；这些属于发布基础设施，不能用仓库内的占位密钥替代。
## 本机体积验证（2026-07-13）

使用同一套 `win-x64`、Release、单文件配置实测：

| 模式 | EXE 大小 | 适用场景 |
| --- | ---: | --- |
| `SelfContained` | 142,024,206 bytes（约 135.4 MiB） | 面向普通玩家的默认安装包，无需预装 Runtime。 |
| `FrameworkDependent` | 2,628,824 bytes（约 2.5 MiB） | 内部分发或可确保预装 .NET 10 Desktop Runtime 的环境。 |

这只是可执行文件大小，不包含安装器压缩率、签名和未来更新包的差异。正式公开发布默认选择 `SelfContained`。
