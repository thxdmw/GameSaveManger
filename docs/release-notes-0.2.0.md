# GameSave Manager 0.2.0

`0.2.0` 是首个带安全自动更新能力的预发布过渡版本。`0.1.0` 尚不能自动升级到该版本，因此本次仍需手动下载并安装；安装完成后，后续版本才可以使用客户端内更新流程。

## 主要变化

- 支持启动后后台检查和设置页手动检查 GitHub 预发布。
- 支持更新下载进度、取消下载、退出安装、启动健康确认和失败回滚。
- 更新安装前同时校验独立 ECDSA 清单、GitHub 资产摘要、`SHA256SUMS.txt`、本地 SHA-256、Windows Authenticode 信任链与发布者证书指纹。
- 主程序、更新引导程序、安装包和卸载程序均使用同一 RSA 3072 位自签发布者证书和 SHA-256 文件摘要签名，并添加可信时间戳。
- 提供公开证书、证书元数据以及安装和移除当前用户信任的脚本。
- 修复并完善添加游戏状态隔离、详情页切换、备份进度、存档配置和卡片交互。

## 首次安装顺序

从项目官方 GitHub Release 下载以下文件，并保持在同一目录：

- `GameSaveManager-Setup-0.2.0.exe`
- `GameSaveManager-Publisher.cer`
- `GameSaveManager-Publisher.json`
- `Install-GameSaveManagerCertificate.ps1`
- `Remove-GameSaveManagerCertificate.ps1`
- `SHA256SUMS.txt`

先用 `SHA256SUMS.txt` 核对所有文件。发布者证书固定 SHA-256 为：

```text
14768BC7D3CF2B1EB5BBE3228ADC8A3D35A1F923CB806B64147D7CFD3BCA8E35
```

在该目录打开 PowerShell，运行：

```powershell
.\Install-GameSaveManagerCertificate.ps1 `
  -CertificatePath .\GameSaveManager-Publisher.cer `
  -InstallerPath .\GameSaveManager-Setup-0.2.0.exe
```

脚本会校验证书固定指纹、有效期、代码签名用途、RSA 密钥长度以及安装包签名。确认输出与官方指纹一致后，输入大写 `TRUST`。脚本只修改当前 Windows 用户的“受信任的根证书颁发机构”和“受信任的发布者”，不要求管理员权限。

证书安装成功后再运行安装包。若 PowerShell 因下载标记阻止脚本，可在核对哈希后执行：

```powershell
Unblock-File .\Install-GameSaveManagerCertificate.ps1
Unblock-File .\Remove-GameSaveManagerCertificate.ps1
```

不要通过关闭全局执行策略或执行来源不明的脚本来绕过安全检查。

## 自签证书的限制

- 证书由项目自行签发，不是受公共 CA 审核的身份凭证；信任决定由用户自己作出。
- 初次下载或信誉积累不足时，Windows SmartScreen 仍可能提示风险。
- 只应从 `https://github.com/thxdmw/GameSaveManger/releases` 下载，并在信任证书前核对 `SHA256SUMS.txt` 和上面的固定指纹。
- 仓库和 Release 只包含公开 `.cer`；PFX 私钥和密码不会公开。
- 移除证书后，客户端会拒绝后续自签安装包的自动更新验证。

不再使用客户端时，可以运行：

```powershell
.\Remove-GameSaveManagerCertificate.ps1 -CertificatePath .\GameSaveManager-Publisher.cer
```

卸载客户端不会自动删除该信任，以避免仍在升级或回滚过程中时破坏签名验证。

## 已知边界

- 本版本为预发布版，仍需在真实 CMS、对象存储、游戏进程和多设备环境中完成人工验收。
- `0.2.0` 只能验证“从 `0.2.0` 安装后更新到下一版本”的基础；完整自动更新和回滚必须在后续版本发布时验收。
- 自签方案不等同于商业代码签名证书；未来如切换到 CA 证书，需要按发布手册执行证书过渡。
