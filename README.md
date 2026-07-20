# GameSave Manager

GameSave Manager 是面向 Windows 的游戏存档管理客户端，使用 .NET 10、WPF 和 C# 构建。客户端通过完整目录扫描、内容寻址和不可变云端快照管理游戏存档，重点保证上传、冲突处理和恢复过程不会静默覆盖用户数据。

> `0.1.0` 预发布版已经提供 Windows 安装包，项目仍处于发布验收阶段。正式用于重要存档前，请先在目标 CMS、对象存储和真实游戏环境中完成 [TODO.md](TODO.md) 中的端到端验收。

## 当前功能

- 通过六步向导添加 Steam、Epic、GOG 或本地 EXE/LNK 游戏。
- 测试启动入口并识别需要监控的游戏进程，支持工作目录、启动参数和管理员权限。
- 使用内置或在线更新的 Ludusavi Manifest、常见目录和运行前后变化学习建议存档位置。
- 在用户预览并确认前禁止同步，支持多个文件存档根目录、Include/Exclude Glob 和 HKCU 注册表存档。
- 使用 SHA-256 构建完整、不可变的 Snapshot Manifest，重复内容只上传一次。
- 支持立即备份、游戏退出后自动备份、上传进度、取消、失败重试和结果摘要。
- 支持云端快照时间线、恢复预览、安全恢复、ZIP 导出、历史快照删除和保留策略。
- 多设备 HEAD 不一致时阻断普通上传，并提供“恢复云端版本”或“保留本机并创建新版本”。
- 支持云端容量、已登录设备、设备撤销和按服务端隔离的登录会话。
- 支持深色/浅色主题、开机启动、系统托盘后台运行和 Windows 安装包。
- 支持启动后后台检查或手动检查 GitHub 预发布；安装前验证独立签名清单、GitHub 资产摘要、`SHA256SUMS.txt`、本地 SHA-256 和 Windows 发布者签名，升级启动失败时尝试恢复上一版本。

## 使用流程

### 1. 登录并添加游戏

在“账户”页配置 CMS 服务端地址并注册或登录。随后点击顶部或“游戏库”中的“添加游戏”，完成六步向导：

1. 扫描 Steam / Epic / GOG，或选择本地 EXE/LNK。
2. 确认游戏名称、启动入口、工作目录、参数和监控进程，并执行测试启动。
3. 自动检测、手动选择或运行游戏学习存档目录。
4. 生成存档预览并确认目录。
5. 选择仅手动备份，或启用游戏退出后自动备份。
6. 检查摘要后创建云端游戏并保存本机配置。

最后一步之前不会创建云端游戏。切换游戏来源时，向导会重置与上一款游戏相关的启动和存档候选状态。

### 2. 备份存档

在“游戏详情”中点击“立即备份”，客户端会重新扫描全部已确认根目录，构建完整 Manifest，只上传服务端缺失的内容对象，再提交快照并推进远端 HEAD。界面会显示阶段、进度和本次上传摘要。

启用“退出后自动备份”后，客户端会监听已验证的游戏进程。游戏退出时如果存档发生变化，会自动执行同一套快照流程。关闭主窗口时选择“最小化到托盘”可让监听继续运行；选择“退出程序”会停止后台监听。

### 3. 在另一台电脑恢复

在新电脑登录同一服务端和账号，添加同一款云端游戏并配置本机存档目录，然后在“时间线”选择快照：

1. 点击“查看恢复内容”确认文件数量和数据量。
2. 点击“恢复所选快照”。
3. 客户端下载并校验全部对象，在同一磁盘构建 staging 目录。
4. 原存档会先改名为安全备份，再由 staging 原子替换。

恢复不会先删除真实存档目录。下载、校验或切换失败时，恢复 Journal 会保留现场并在下次启动时执行可证明安全的恢复或提示人工处理。

### 4. 处理多设备冲突

普通上传要求本地记录的 HEAD 与远端 HEAD 一致。其他设备已产生新快照时，客户端会阻断上传：

- “恢复云端版本”：将远端版本安全恢复到本机。
- “保留本机并创建新版本”：以当前远端 HEAD 为父版本提交本机内容，原有历史版本仍保留。

## 页面入口

| 页面 | 用途 |
| --- | --- |
| 首页 | 查看最近游戏、运行/保护状态和最近快照，快速启动或进入存档管理。 |
| 游戏库 | 添加、搜索和管理游戏；卡片菜单可进入存档管理或删除游戏及其本地配置。 |
| 游戏详情 | 配置启动入口和存档规则，立即备份，启用/停用退出后自动备份。 |
| 时间线 | 查看云端快照，预览、导出、删除、恢复或处理多设备冲突。 |
| 设备 | 查看云端容量与登录设备，并撤销不再使用的设备凭据。 |
| 账户 | 配置服务端地址，注册、登录和退出登录。 |
| 设置 | 切换主题与开机启动，检查客户端及存档识别规则更新，管理快照保留策略和第三方许可证。 |
| 使用说明 | 查看操作流程、常见问题，以及默认折叠的完整按钮说明。 |

## 技术栈

| 分类 | 技术 |
| --- | --- |
| 桌面客户端 | .NET 10、WPF、C# |
| 本地存储 | SQLite |
| 凭据存储 | Windows Credential Manager |
| 内容校验 | SHA-256 |
| 云端通信 | HTTP API、HTTPS、预签名对象下载 |
| 发布 | `dotnet publish`、Inno Setup 6、Authenticode、ECDSA 签名更新清单 |
| 持续集成 | GitHub Actions、Windows Runner |

## 项目结构

```text
GameSaveManger
├── src
│   ├── GameSaveManager.App              # WPF 界面、ViewModel 和应用组合根
│   ├── GameSaveManager.Application      # 同步、恢复、快照和启动编排
│   ├── GameSaveManager.Domain           # 游戏、快照等领域模型
│   ├── GameSaveManager.Infrastructure   # SQLite、HTTP、文件系统、凭据和 Windows 实现
│   └── GameSaveManager.UpdateBootstrapper # 独立更新、启动确认和失败回滚
├── tests
│   └── GameSaveManager.Verification     # 协议、安全边界、迁移和 WPF 冒烟验证
├── scripts                              # Windows 发布、签名与安装脚本
├── tools                                # 发布清单生成和签名工具
├── installer                            # Inno Setup 安装器定义
├── docs                                 # 构建和发布说明
├── GameSaveManager.sln
└── TODO.md
```

## 架构边界

- `GameSaveManager.Domain`：不可变领域模型，不依赖 WPF、SQLite、HTTP 或 Windows API。
- `GameSaveManager.Application`：编排 Manifest、云端同步、安全恢复、快照导出和游戏启动，依赖抽象接口。
- `GameSaveManager.Infrastructure`：实现 Windows 文件系统、SQLite、HTTP API、Credential Manager、游戏发现和进程监控。
- `GameSaveManager.App`：负责界面、ViewModel、命令绑定和依赖组装，不承载核心同步规则。

## 数据与安全约束

### 完整快照

`FileSystemWatcher` 只负责标记目录发生变化，不能作为快照事实来源。创建快照时会重新扫描全部已确认目录：

```text
完整扫描存档根目录
    ↓
使用 size + LastWriteTimeUtc 查询 Hash Cache
    ↓
缓存失效时流式计算 SHA-256
    ↓
再次检查文件稳定性
    ↓
构建完整、不可变的 Manifest
```

多根目录文件使用 `rootId/相对路径` 写入 Manifest。扫描会拒绝绝对路径、`..` 路径穿越和重解析点；注册表存档仅接受用户明确配置的 HKCU 键。

### 同步与 HEAD

```text
读取远端 HEAD
    ↓
localHead == remoteHead ?
    ├─ 否：阻断普通上传并进入冲突处理
    └─ 是：构建完整 Manifest
              ↓
          查询并上传缺失内容对象
              ↓
          提交快照并以 CAS 推进 HEAD
              ↓
          更新本地 sync_state
```

服务端已有 HEAD 且 Manifest 完全一致时，同步为幂等 no-op，不创建重复快照。设备 Token 只发送给 GameSave 服务端，不会转发给对象存储；对象下载使用服务端签发的短时预签名地址。

### 安全恢复

恢复先下载并验证所有对象，再构建同盘 staging。真实存档会先改名为安全备份，随后 staging 才会切换为真实目录，最后再次校验 SHA-256。注册表恢复与文件恢复位于同一事务中，并在修改前导出本机安全副本。

恢复日志位于：

```text
%LOCALAPPDATA%\GameSaveManager\restore\{transactionId}\journal.json
```

### 服务端隔离

服务端地址只接受绝对 `http` 或 `https` URL；远程地址必须使用 HTTPS，HTTP 仅允许回环地址。规范化地址会生成稳定 `serverKey`，用于隔离 Credential Manager 中的设备 Token，以及 SQLite 中的游戏配置和同步 HEAD。

## 本地数据

默认数据库：

```text
%LOCALAPPDATA%\GameSaveManager\data\gamesave.db
```

主要表包括：

- `file_hash_cache`：文件 SHA-256 缓存。
- `sync_state`：按 `server_key + game_id` 保存最后确认的远端 HEAD。
- `client_setting`：稳定设备 ID 和客户端设置。
- `local_game_profile`：按服务端和游戏保存启动入口、存档规则、进程及自动备份开关。
- `schema_version`：本地数据库版本与迁移状态。

设备 Token 不写入 SQLite，统一保存到 Windows Credential Manager。

客户端更新安装包暂存在 `%LOCALAPPDATA%\GameSaveManager\updates\版本号`。未完成或校验不一致的文件会被删除；只有签名清单、GitHub 资产摘要、`SHA256SUMS.txt`、本地 SHA-256 和 Windows 发布者证书全部一致时才允许更新。事务与启动确认位于 `updates\transactions`，上一版本安装包位于 `rollback`。

## 环境与构建

- Windows 10/11。
- .NET 10 SDK。
- 可访问且数据库已初始化的 GameSave CMS 服务端。
- 远程部署使用 HTTPS，并正确配置私有对象存储。

```powershell
dotnet restore .\GameSaveManager.sln
dotnet build .\GameSaveManager.sln -c Debug
dotnet run --project .\tests\GameSaveManager.Verification\GameSaveManager.Verification.csproj -c Debug --no-build
```

验证项目当前覆盖服务端地址规范化、本地 SQLite 迁移和隔离、协议契约、CMS 时间转换、启动安全、进程识别、添加向导状态、注册表预览、恢复中断处理、错误重试与日志，以及 WPF 页面加载和绑定错误检查。它不替代真实 CMS、对象存储、游戏进程和多设备环境下的端到端验收。

## Windows 发布

生成 `win-x64` 自包含发布物：

```powershell
.\scripts\publish-windows.ps1
```

生成依赖 .NET 10 Desktop Runtime 的发布物：

```powershell
.\scripts\publish-windows.ps1 -DeploymentMode FrameworkDependent
```

安装 Inno Setup 6 后生成安装包：

```powershell
.\scripts\build-installer.ps1
```

脚本会自动读取根目录 `Directory.Build.props`，不再从命令行传入版本号。详细参数和验收步骤见 [构建说明](docs/build.md)、[Windows 发布说明](docs/release-windows.md)、[发布签名与恢复手册](docs/signing-and-recovery.md) 与 [版本管理流程](docs/versioning.md)。当前安装包可在 [GitHub 预发布页](https://github.com/thxdmw/GameSaveManger/releases) 下载；下一次安全发布前需先配置正式代码签名证书和 GitHub Secrets，并在干净系统完成人工升级与回滚验收。

## 后续任务

当前未完成事项统一维护在 [TODO.md](TODO.md)。
