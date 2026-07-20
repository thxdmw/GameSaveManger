# 发布签名与恢复手册

本手册描述 `0.2.0` 自签代码签名证书、独立更新清单密钥和升级恢复流程。任何私钥、PFX、密码或 DPAPI 密文都不能提交到仓库、Release、Issue、日志或聊天记录。

## 一、自签代码签名证书

当前发布者证书参数：

| 项目 | 值 |
| --- | --- |
| 主题 | `CN=GameSave Manager Self-Signed Publisher` |
| 算法 | RSA 3072 / SHA-256 |
| 扩展用途 | Code Signing (`1.3.6.1.5.5.7.3.3`) |
| 证书 SHA-256 | `14768BC7D3CF2B1EB5BBE3228ADC8A3D35A1F923CB806B64147D7CFD3BCA8E35` |
| SHA-1 指纹 | `C2299BA7748C11E2D4D8FA5080F38990B8FB3D05` |
| 到期日 | 2031-07-20 |

仓库只跟踪公开证书和非敏感元数据：

```text
certificates\GameSaveManager-Publisher.cer
certificates\GameSaveManager-Publisher.json
```

发布者本机的私密文件默认位于：

```text
%USERPROFILE%\.gamesavemanager-secrets\GameSaveManager-Publisher.pfx
%USERPROFILE%\.gamesavemanager-secrets\GameSaveManager-Publisher.password.dpapi
```

密码文件由当前 Windows 用户的 DPAPI 加密，PFX 与密码文件 ACL 只允许当前用户读取。至少再保存两份加密离线备份，并把备份介质分开保管。生成全新证书使用：

```powershell
.\scripts\new-self-signed-publisher-certificate.ps1
```

脚本拒绝覆盖现有私钥。不要为了“重试”删除现有 PFX；一旦更换证书，已安装客户端的发布者固定和用户信任都需要执行正式轮换。

## 二、用户信任边界

自签证书没有公共 CA 信任链。用户必须先核对官方指纹，再运行：

```powershell
.\Install-GameSaveManagerCertificate.ps1 `
  -CertificatePath .\GameSaveManager-Publisher.cer `
  -InstallerPath .\GameSaveManager-Setup-0.2.0.exe
```

脚本把公开证书导入当前用户的 `Root` 和 `TrustedPublisher`，不会访问本地计算机证书库，也不要求管理员权限。安装包参数用于同时确认 Authenticode 签名有效且叶证书就是被固定的发布者证书。

移除信任使用：

```powershell
.\Remove-GameSaveManagerCertificate.ps1 -CertificatePath .\GameSaveManager-Publisher.cer
```

卸载客户端不会自动移除证书，因为自动更新失败回滚仍可能依赖同一发布者信任。自签证书不能保证消除 SmartScreen 提示，用户仍必须只从官方 Release 下载并核对 `SHA256SUMS.txt`。

## 三、GitHub Actions Secrets

发布工作流需要以下仓库 Actions Secrets：

| Secret | 内容 |
| --- | --- |
| `WINDOWS_SIGNING_PFX_BASE64` | 自签 PFX 原始字节的 Base64 |
| `WINDOWS_SIGNING_PFX_PASSWORD` | PFX 密码明文，仅保存在 GitHub Secret 中 |
| `UPDATE_MANIFEST_SIGNING_KEY_BASE64` | ECDSA 清单私钥 `.pk8` 原始字节的 Base64 |

先通过 `gh auth login` 登录有仓库管理权限的 GitHub 账号，然后运行：

```powershell
.\scripts\set-release-secrets.ps1
```

脚本从 DPAPI 密码文件解密密码、检查 PFX 与仓库公开证书完全一致，再通过 GitHub CLI 标准输入写入三个 Secret。它不会打印密码、私钥或 Base64，也不会把明文写入新文件。

## 四、独立更新清单密钥

仓库包含清单验证公钥；对应私钥默认位于仓库外：

```text
%USERPROFILE%\.gamesavemanager-secrets\update-manifest-private-key.pk8
```

如需为全新产品线生成密钥，可运行：

```powershell
.\scripts\new-update-signing-key.ps1
```

脚本拒绝覆盖现有密钥，并把私钥 ACL 限制为当前 Windows 用户。至少保存两份加密离线备份。仓库只保存 `src/GameSaveManager.Infrastructure/Updates/Assets/*.pem` 公钥。

## 五、发布时发生什么

1. 工作流解码并导入 PFX，确认它与仓库公开证书及固定元数据完全一致。
2. Runner 临时把公开自签叶证书加入当前用户 `TrustedPeople`，用于本次 SignTool 直接信任和签名复验；不把发布者证书提升为 Runner 的根 CA。
3. SignTool 使用 SHA-256 文件摘要和 DigiCert 可信 Authenticode 时间戳签名主程序、更新引导程序、安装包和卸载程序，并使用 `/tw` 强制验证时间戳。
4. 发布工具把安装包 URL、大小、SHA-256 和发布者证书 SHA-256 写入清单，并用独立 ECDSA 私钥签名。
5. 工作流用仓库公钥回验清单，生成覆盖除校验文件自身以外全部附件的 `SHA256SUMS.txt`，再一次性创建不可覆盖的 GitHub 预发布。
6. `always()` 清理 Runner 的 PFX、清单私钥以及临时加入 `My` 和 `TrustedPeople` 的证书。

任何一步失败都不会创建 Release 附件。

## 六、轮换

### 自签代码签名证书

自签证书没有 CA 吊销通道，因此轮换必须至少跨两个版本：

1. 在旧证书仍安全有效时生成新证书，把新公开证书及指纹随过渡版本发布，并让客户端同时接受旧、新发布者。
2. 用户安装过渡版本并明确导入新证书后，下一版本才改用新 PFX 签名。
3. 至少保留旧发布者接受逻辑一个发布周期；确认不再需要跨版本升级后才能移除。

签名清单固定每个安装包的叶证书 SHA-256，但 `WinVerifyTrust` 仍要求用户系统信任新自签证书。旧版本已有可信时间戳的附件不得重新签署或覆盖。

### 更新清单密钥

清单密钥也必须采用两版本过渡：

1. 仍用旧私钥发布一个过渡客户端，同时把新公钥 PEM 加入 `Updates/Assets`。
2. 确认过渡版本已广泛安装后，GitHub Secret 切换到新私钥。
3. 至少再保留旧公钥一个发布周期。

## 七、灾难恢复

- PFX 泄露：立即禁用发布工作流并删除 GitHub PFX Secrets。自签证书不能通过公共 CA 吊销，必须发布安全公告、生成新证书并执行人工信任迁移；不得覆盖旧附件。
- 清单私钥泄露：立即禁用发布工作流并删除 Secret。若旧私钥已不可信，只能依靠已固定且受信的 Authenticode 发布者，通过人工下载安装包重置清单公钥。
- 私钥丢失：从加密离线备份恢复。没有备份时，旧客户端无法验证新清单或新发布者，只能人工安装信任重置版本。
- 自动更新失败：查看 `%LOCALAPPDATA%\GameSaveManager\updates\transactions\*.state`。`rolled_back` 表示已恢复，`rollback_failed` 表示需要手动运行上一版本安装包；程序更新不会删除游戏存档、SQLite 或 Credential Manager 凭据。
- 回滚安装包损坏或缺失：从对应不可变 GitHub Release 重新下载，验证哈希、清单和发布者签名后手动安装。

每次证书或清单密钥轮换都必须在隔离 Windows 虚拟机演练正常升级、启动崩溃回滚、离线回滚和人工恢复，并记录日期与结果。
