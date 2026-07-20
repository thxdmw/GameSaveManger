# 发布签名与恢复手册

本手册只描述发布密钥和更新恢复。私钥永远不能提交到仓库，也不能粘贴到 Issue、Release、日志或聊天记录。

## 一、准备正式代码签名证书

向受 Windows 信任的 CA 申请个人或组织代码签名证书，并导出为包含私钥的 PFX。证书必须包含“代码签名”扩展用途。当前 GitHub 工作流适用于可导出的 PFX；如果购买的是硬件令牌、EV 证书或云签名服务，需要把工作流的 SignTool 调用改成该服务的认证方式，不能导出或伪造私钥。

在 GitHub 仓库的 Actions Secrets 中配置：

| Secret | 内容 |
| --- | --- |
| `WINDOWS_SIGNING_PFX_BASE64` | PFX 文件原始字节的 Base64 |
| `WINDOWS_SIGNING_PFX_PASSWORD` | PFX 导出密码 |
| `UPDATE_MANIFEST_SIGNING_KEY_BASE64` | ECDSA 清单私钥 `.pk8` 原始字节的 Base64 |

PFX Base64 可在本机生成后直接写入 GitHub CLI，不落地新的明文副本：

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\安全目录\codesign.pfx')) |
  gh secret set WINDOWS_SIGNING_PFX_BASE64
```

PFX 密码建议在 GitHub 网页的 Secrets 页面手工填写，避免进入 PowerShell 历史。

## 二、更新清单密钥

仓库已经包含清单验证公钥；对应私钥默认位于仓库外：

```text
%USERPROFILE%\.gamesavemanager-secrets\update-manifest-private-key.pk8
```

如果需要为全新产品线生成密钥，可运行：

```powershell
.\scripts\new-update-signing-key.ps1
```

脚本拒绝覆盖现有密钥，并把私钥 ACL 限制为当前 Windows 用户。将现有私钥写入 GitHub Secret：

```powershell
[Convert]::ToBase64String(
  [IO.File]::ReadAllBytes("$env:USERPROFILE\.gamesavemanager-secrets\update-manifest-private-key.pk8")) |
  gh secret set UPDATE_MANIFEST_SIGNING_KEY_BASE64
```

至少保存两份加密离线备份，并分别保管恢复密码。仓库只保存 `src/GameSaveManager.Infrastructure/Updates/Assets/*.pem` 公钥。

## 三、发布时发生什么

1. 工作流导入 PFX 到 Runner 当前用户证书库，并检查代码签名用途。
2. SignTool 使用 SHA-256 文件摘要、RFC 3161 时间戳和 SHA-256 时间戳摘要签名。
3. Inno Setup 同时签名安装包和卸载程序。
4. 发布工具把安装包 URL、大小、SHA-256 和发布者证书 SHA-256 写入清单，并用独立 ECDSA 私钥签名。
5. 工作流用仓库公钥回验清单，随后一次性创建不可变 Release。
6. `always()` 清理 Runner 证书和临时私钥文件。

任何一步失败都不会发布附件。

## 四、轮换

### 代码签名证书

在旧证书到期前申请新证书，替换两个 PFX Secret 后发布新版本。旧客户端信任独立清单，清单固定新安装包的证书指纹，因此允许正常换证；Windows 仍必须信任新证书链。旧版本已有可信时间戳的签名不应重新签署或覆盖。

### 更新清单密钥

清单密钥必须采用两版本过渡：

1. 仍用旧私钥发布一个过渡客户端，同时把新公钥 PEM 加入 `Updates/Assets`。
2. 确认过渡版本已广泛安装后，GitHub Secret 切换到新私钥。
3. 至少再保留旧公钥一个发布周期；确认没有需要跨版本升级的客户端后才能删除。

客户端会接受内置公钥集合中的任意一个有效签名，因此轮换期间旧、新清单均可验证。

## 五、灾难恢复

- PFX 泄露：立即请求 CA 吊销，删除 GitHub Secrets，审计发布记录，取得新证书后递增版本发布；不要覆盖旧附件。
- 清单私钥泄露：立即禁用发布工作流，先用仍可信的旧渠道发布内置新公钥的过渡客户端；若旧私钥不可再信任，只能通过受信 Authenticode 安装包和人工下载完成信任重置。
- 清单私钥丢失：从离线备份恢复。没有备份时，旧客户端无法验证新清单，只能人工安装带新公钥的客户端。
- 自动更新失败：查看 `%LOCALAPPDATA%\GameSaveManager\updates\transactions\*.state`。`rolled_back` 表示已恢复，`rollback_failed` 表示需要手动运行上一版本安装包；用户数据目录不会随程序覆盖安装被删除。
- 回滚安装包损坏或缺失：从对应不可变 GitHub Release 重新下载并验证签名后手动安装。

每次证书或密钥轮换都要在隔离 Windows 虚拟机演练一次正常升级、启动崩溃回滚、离线回滚和手工恢复，并记录演练日期与结果。
