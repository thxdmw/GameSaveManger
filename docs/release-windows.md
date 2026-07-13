# Windows 发布与安装

客户端采用 .NET 10 自包含单文件发布：目标电脑无需预装 .NET Runtime。默认架构是 `win-x64`；ARM Windows 可改用 `win-arm64`。

## 生成发布目录

在仓库根目录执行：

```powershell
.\scripts\publish-windows.ps1
```

输出目录为 `artifacts\publish\win-x64`。发布脚本没有启用 trimming：WPF、SQLite 原生库和系统凭据调用对动态加载较敏感，先保证可用性和可诊断性，再根据真实发布物做专项裁剪验证。

## 生成安装包

先安装 Inno Setup 6，然后执行：

```powershell
.\scripts\build-installer.ps1 -Version 0.1.0
```

安装包会输出到 `artifacts\installer`。安装范围为当前 Windows 用户，不需要管理员权限；程序文件位于 `%LOCALAPPDATA%\Programs\GameSaveManager`，游戏存档、SQLite、日志及凭据仍分别使用应用数据目录和 Windows Credential Manager。

如果 Inno Setup 不在默认目录，可显式提供编译器：

```powershell
.\scripts\build-installer.ps1 `
  -Version 0.1.0 `
  -InnoSetupCompiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

## 发布前检查

```powershell
dotnet build .\GameSaveManager.V2.sln --no-restore
dotnet run --project .\tests\GameSaveManager.Verification\GameSaveManager.Verification.csproj --no-build
```

发布到真实服务端前还应运行 CMS 仓库的 Docker Compose 冒烟链路。远程服务端地址必须使用 HTTPS；客户端只允许 `localhost` 和 `127.0.0.1` 使用 HTTP。

## 签名与更新

安装包当前不内置签名证书、私钥或自动更新地址。正式对外分发前，应在受保护的 CI 凭据库中配置代码签名证书，并使用 `signtool` 对 EXE 与安装包签名、时间戳。自动更新也应由 HTTPS 下载源、已签名的版本清单和可回滚策略组成；这些属于发布基础设施，不能用仓库内的占位密钥替代。