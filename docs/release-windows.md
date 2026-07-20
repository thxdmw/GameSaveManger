# Windows 发布与安装

客户端默认发布为 `win-x64`、.NET 10 自包含单文件，不要求目标电脑预装 .NET Runtime。普通本地构建可保持未签名，GitHub Release 安全发布则强制要求正式代码签名证书、可信时间戳和独立更新清单签名，任何密钥缺失都会终止发布。

## 本地发布

生成自包含发布目录：

```powershell
.\scripts\publish-windows.ps1
```

生成依赖 .NET 10 Desktop Runtime 的调试分发目录：

```powershell
.\scripts\publish-windows.ps1 -DeploymentMode FrameworkDependent
```

安装 Inno Setup 6 后生成本地安装包：

```powershell
.\scripts\build-installer.ps1
```

输出位于 `artifacts\installer`。安装范围为当前 Windows 用户，程序位于 `%LOCALAPPDATA%\Programs\GameSaveManager`；SQLite、日志、内容缓存和更新事务仍在 `%LOCALAPPDATA%\GameSaveManager`，设备凭据仍在 Windows Credential Manager。

本地显式签名构建可使用：

```powershell
.\scripts\build-installer.ps1 `
  -SignToolPath 'C:\Program Files (x86)\Windows Kits\10\bin\<版本>\x64\signtool.exe' `
  -SigningCertificateThumbprint '<当前用户证书库中的 SHA-1 证书指纹>'
```

这里的 SHA-1 指纹只用于从证书库选择证书；文件摘要、清单签名和时间戳摘要均使用 SHA-256。脚本会签名主程序、安全更新引导程序、安装包和卸载程序，并使用 `/pa /all /tw` 再次验证。

## 安全更新边界

客户端只接受同时满足以下条件的更新：

1. 版本符合 SemVer，来自固定 GitHub 仓库的 HTTPS Release。
2. `update-manifest.json` 能由客户端内置的 ECDSA P-256 公钥验证。
3. 清单、GitHub 资产 digest、`SHA256SUMS.txt` 和下载文件的 SHA-256 完全一致。
4. Windows `WinVerifyTrust` 确认安装包 Authenticode 信任链有效。
5. 安装包叶证书的 SHA-256 指纹与签名清单固定的发布者证书一致。

用户确认后，客户端复制并验证随程序安装的安全更新引导程序，然后完全退出。引导程序静默安装新版本并启动客户端；新版本必须在 90 秒内写入受控健康确认文件。失败时，引导程序使用 `%LOCALAPPDATA%\GameSaveManager\rollback` 中保留的上一版本安装包恢复。更新事务记录位于 `%LOCALAPPDATA%\GameSaveManager\updates\transactions`，不会删除游戏存档、SQLite 或 Credential Manager 凭据。

首个包含此安全更新机制的版本仍需手动下载安装。该版本安装完成后会保留自己的回滚安装包，后续版本才能完成全自动失败恢复。

证书、GitHub Secrets、密钥轮换和灾难恢复步骤见 [发布签名与恢复手册](signing-and-recovery.md)。版本号、标签和发布说明规则见 [版本管理与发布流程](versioning.md)。

## 发布前一次性验证

```powershell
dotnet restore .\GameSaveManager.sln
dotnet build .\GameSaveManager.sln -c Release --no-restore
dotnet test .\GameSaveManager.sln -c Release --no-build
dotnet run --project .\tests\GameSaveManager.Verification\GameSaveManager.Verification.csproj -c Release --no-build
.\scripts\build-installer.ps1
```

正式发布还应在干净 Windows 虚拟机中抽查签名安装、覆盖升级、健康确认、故障回滚和卸载，并在真实 CMS 与对象存储环境完成端到端存档验收。
