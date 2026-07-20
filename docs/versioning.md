# 版本管理与发布流程

GameSave Manager 使用语义化版本 `主版本.次版本.修订版本`。当前处于 `0.x` 预发布阶段，版本号唯一来源是仓库根目录的 `Directory.Build.props`。

## 版本规则

- 修订版本：仅修复缺陷，例如 `0.1.0` → `0.1.1`。
- 次版本：增加向后兼容功能，例如 `0.1.1` → `0.2.0`。
- 主版本：达到稳定承诺或引入不兼容变更，例如 `0.9.0` → `1.0.0`。
- 开发预览：在 `GameSaveManagerVersionSuffix` 填写 `preview.1`，完整版本为 `0.2.0-preview.1`。

`GameSaveManagerReleaseChannel` 只负责客户端展示。不要在项目文件、脚本或工作流中再次手写版本号。

## 准备下一版本

1. 修改 `Directory.Build.props` 的版本前缀和可选后缀。
2. 在 `CHANGELOG.md` 的“未发布”区域记录用户可感知变更。
3. 创建 `docs/release-notes-版本号.md`。
4. 安装仓库固定的公开自签证书信任，并一次性完成 Release 构建和验证：

```powershell
dotnet restore .\GameSaveManager.sln
dotnet build .\GameSaveManager.sln -c Release --no-restore
dotnet test .\GameSaveManager.sln -c Release --no-build
dotnet run --project .\tests\GameSaveManager.Verification\GameSaveManager.Verification.csproj -c Release --no-build
.\scripts\test-release-version.ps1 -Tag v0.2.0
.\scripts\Install-GameSaveManagerCertificate.ps1 -CertificatePath .\certificates\GameSaveManager-Publisher.cer -Force
.\scripts\build-installer.ps1 -SignToolPath '<SignTool 路径>' -SigningCertificateThumbprint 'C2299BA7748C11E2D4D8FA5080F38990B8FB3D05'
```

5. 运行 `.\scripts\set-release-secrets.ps1` 配置三个 GitHub Actions Secrets，提交并推送发布提交。确认本地签名构建与干净账户安装验收通过后，再创建不可移动的签名标签：

```powershell
git push origin master
git tag -a v0.2.0 -m "发布 0.2.0 预发布版"
git push origin v0.2.0
```

标签推送后，`.github/workflows/release.yml` 会核对 PFX、仓库公开证书与固定元数据，执行完整验证，签名主程序、更新引导程序、安装包和卸载程序，生成覆盖除校验文件自身以外全部附件的 `SHA256SUMS.txt`、签名更新清单，并创建 GitHub 预发布。工作流禁止覆盖已存在 Release 的附件；发布失败且 Release 已经创建时必须修复问题并递增版本号。

## 发布纪律

- 已公开的提交、标签、版本号和 Release 附件不可移动或覆盖。
- 安装包名称、程序集版本、安装器版本、签名清单和 GitHub 标签必须一致。
- GitHub Secrets 只能由受控发布环境读取，私钥不得写入仓库、日志或构建产物。
- 自动发布成功后仍要在干净 Windows 环境抽查安装、启动、升级、失败恢复和卸载。
- `0.2.0` 是首个具备安全更新能力的版本，用户必须先核对并信任自签发布者证书，再手动安装；再下一版本才可实际验收自动更新和回滚。

密钥配置与轮换见 [发布签名与恢复手册](signing-and-recovery.md)。
