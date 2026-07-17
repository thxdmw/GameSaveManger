# 版本管理与发布流程

GameSave Manager 使用语义化版本 `主版本.次版本.修订版本`。当前仍处于 `0.x` 预发布阶段，版本号的唯一来源是仓库根目录的 `Directory.Build.props`。

## 版本号规则

- 修订版本：只修复缺陷且不改变既有用法，例如 `0.1.0` → `0.1.1`。
- 次版本：增加向后兼容的功能，例如 `0.1.1` → `0.2.0`。
- 主版本：产品达到稳定承诺或引入不兼容变更时增加，例如 `0.9.0` → `1.0.0`。
- 开发预览：需要区分尚未发布的安装包时，填写 `GameSaveManagerVersionSuffix`，例如 `preview.1`；完整版本会成为 `0.2.0-preview.1`。

`GameSaveManagerReleaseChannel` 是客户端设置页展示的发布通道。当前 `0.x` 安装包统一使用“预发布”。不要在项目文件、脚本或工作流中再次手写版本号。

## 开始下一版本

1. 根据变更范围确定下一版本号。
2. 修改 `Directory.Build.props` 中的 `GameSaveManagerVersionPrefix`；需要开发预览时同时设置 `GameSaveManagerVersionSuffix`。
3. 在 `CHANGELOG.md` 的“未发布”区域记录用户可感知的变更。
4. 开发期间通过设置页确认程序集版本和发布通道正确。

## 发布检查

发布前一次性完成构建和验证：

```powershell
dotnet restore .\GameSaveManager.sln
dotnet build .\GameSaveManager.sln -c Release --no-restore
dotnet test .\GameSaveManager.sln -c Release --no-build
dotnet run --project .\tests\GameSaveManager.Verification\GameSaveManager.Verification.csproj -c Release --no-build
.\scripts\build-installer.ps1
```

然后完成以下整理：

1. 清空版本后缀，确认 `Directory.Build.props` 中的正式版本，例如 `0.2.0`。
2. 将“未发布”内容移动到带日期的版本标题下。
3. 创建 `docs/release-notes-版本号.md`。
4. 执行标签校验：

```powershell
.\scripts\test-release-version.ps1 -Tag v0.2.0
```

5. 提交并推送发布提交，再创建并推送标签：

```powershell
git tag -a v0.2.0 -m "发布 0.2.0 预发布版"
git push origin master
git push origin v0.2.0
```

标签推送后，`.github/workflows/release.yml` 会重新校验标签和版本，执行 Release 构建及验证，生成安装包和 `SHA256SUMS.txt`，并创建 GitHub 预发布。如果自动流程中断，可在 GitHub Actions 手动运行同一工作流并填写已经存在的标签。

## 版本纪律

- 已公开的标签和版本号不可移动、覆盖或重复使用；修复后发布新的修订版本。
- 标签必须指向准备发布的提交，标签版本必须与 `Directory.Build.props` 完全一致。
- 每个版本必须有独立发布说明，安装包名称、程序集版本、安装器版本和 GitHub 标签必须一致。
- 自动发布成功后仍需在一台干净 Windows 环境完成安装、启动、升级和卸载抽查。
- 客户端会从 GitHub Releases 检查更新，并要求 GitHub 资产 digest、`SHA256SUMS.txt` 和本地文件摘要一致；代码签名与独立签名更新清单仍属于后续阶段。
