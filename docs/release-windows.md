# Windows 发布与安装

客户端默认发布为 `win-x64`、.NET 10 自包含单文件，不要求目标电脑预装 .NET Runtime。普通本地构建可保持未签名，GitHub Release 安全发布则强制要求仓库固定的自签代码签名证书、可信时间戳和独立更新清单签名，任何密钥缺失或证书不一致都会终止发布。

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

## 本地签名构建

先生成或导入仓库对应的自签 PFX，并把公开证书安装到当前用户信任库：

```powershell
.\scripts\Install-GameSaveManagerCertificate.ps1 `
  -CertificatePath .\certificates\GameSaveManager-Publisher.cer `
  -Force
```

然后执行：

```powershell
.\scripts\build-installer.ps1 `
  -SignToolPath 'C:\Program Files (x86)\Windows Kits\10\bin\<版本>\x64\signtool.exe' `
  -SigningCertificateThumbprint 'C2299BA7748C11E2D4D8FA5080F38990B8FB3D05'
```

这里的 SHA-1 指纹只用于从证书库选择证书；文件摘要、证书固定、清单签名和时间戳摘要均使用 SHA-256。脚本会签名主程序、安全更新引导程序、安装包和卸载程序，并使用 `/pa /all /tw` 再次验证。

## 用户首次安装

`0.2.0` 是首个带安全自动更新能力的版本，但 `0.1.0` 不能自动升级到它。用户必须从官方 Release 下载公开证书、信任脚本、安装包和 `SHA256SUMS.txt`，核对哈希和固定证书指纹后，先运行证书安装脚本，再运行安装包。详细步骤见 [0.2.0 发布说明](release-notes-0.2.0.md)。

公开 `.cer` 可以提交到仓库并作为 Release 附件；PFX、密码、DPAPI 密文和清单私钥绝不能公开。自签名不会自动消除 SmartScreen 提示，也不等同于公共 CA 对发布者身份的审核。

## 安全更新边界

客户端只接受同时满足以下条件的更新：

1. 版本符合 SemVer，来自固定 GitHub 仓库的 HTTPS Release。
2. `update-manifest.json` 能由客户端内置的 ECDSA P-256 公钥验证。
3. 清单、GitHub 资产 digest、`SHA256SUMS.txt` 和下载文件的 SHA-256 完全一致。
4. Windows `WinVerifyTrust` 确认安装包 Authenticode 信任链有效；自签方案要求用户仍信任固定发布者证书。
5. 安装包叶证书的 SHA-256 指纹与签名更新清单固定的发布者证书一致。

用户确认后，客户端复制并验证随程序安装的安全更新引导程序，然后完全退出。引导程序静默安装新版本并启动客户端；新版本必须在 90 秒内写入受控健康确认文件。失败时，引导程序使用 `%LOCALAPPDATA%\GameSaveManager\rollback` 中保留的上一版本安装包恢复。更新事务记录位于 `%LOCALAPPDATA%\GameSaveManager\updates\transactions`，不会删除游戏存档、SQLite 或 Credential Manager 凭据。

`0.2.0` 安装完成后会保留自己的回滚安装包；发布下一版本时才能完成一次真实的客户端内自动更新和失败回滚验收。

证书、GitHub Secrets、密钥轮换和灾难恢复步骤见 [发布签名与恢复手册](signing-and-recovery.md)。版本号、标签和发布说明规则见 [版本管理与发布流程](versioning.md)。

## 发布前一次性验证

```powershell
dotnet restore .\GameSaveManager.sln
dotnet build .\GameSaveManager.sln -c Release --no-restore
dotnet test .\GameSaveManager.sln -c Release --no-build
dotnet run --project .\tests\GameSaveManager.Verification\GameSaveManager.Verification.csproj -c Release --no-build
.\scripts\test-release-version.ps1 -Tag v0.2.0
.\scripts\build-installer.ps1 -SignToolPath '<SignTool 路径>' -SigningCertificateThumbprint 'C2299BA7748C11E2D4D8FA5080F38990B8FB3D05'
```

正式发布还应在干净 Windows 虚拟机中抽查公开证书安装与移除、签名安装、覆盖升级、健康确认、故障回滚和卸载，并在真实 CMS 与对象存储环境完成端到端存档验收。
