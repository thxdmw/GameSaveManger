# GameSave Manager

GameSave Manager 是一个面向 Windows 的游戏存档管理客户端，使用 .NET 10、WPF 和 C# 构建。项目通过内容寻址、不可变快照和云端时间线管理游戏存档，重点保证同步、冲突处理和恢复过程不会静默覆盖用户数据。

> 当前项目仍在开发和验证阶段。正式用于重要存档前，请先完成真实服务端、多设备冲突和异常恢复测试。

## 核心能力

- 使用 SHA-256 为文件内容生成稳定身份，重复内容只需上传一次。
- 完整扫描存档目录并生成不可变 Snapshot Manifest。
- 本地 HEAD 与远端 HEAD 不一致时阻断普通上传，避免覆盖其他设备版本。
- 支持云端快照时间线、历史版本删除、保留策略和存储配额展示。
- 使用下载校验、同盘 staging、安全备份和恢复 Journal 完成安全恢复。
- 支持恢复前预览、快照导出、同步进度、取消和失败重试。
- 支持 Steam、Epic、GOG 游戏发现，以及存档目录候选路径建议。
- 支持游戏退出后自动快照、多游戏自动同步和开机启动。
- 支持设备列表、设备撤销和按服务端隔离的登录会话恢复。
- 支持深色/浅色主题和 Windows 安装包构建。

## 技术栈

| 分类 | 技术 |
| --- | --- |
| 客户端 | .NET 10、WPF、C# |
| 本地存储 | SQLite |
| 凭据存储 | Windows Credential Manager |
| 内容校验 | SHA-256 |
| 云端通信 | HTTP API、HTTPS、预签名对象下载 |
| 发布 | `dotnet publish`、Inno Setup 6 |
| CI | GitHub Actions、Windows Runner |

## 项目结构

```text
GameSaveManger
├── src
│   ├── GameSaveManager.App              # WPF 界面、ViewModel、应用组合根
│   ├── GameSaveManager.Application      # 同步、恢复、快照等应用服务
│   ├── GameSaveManager.Domain           # 游戏、快照等领域模型
│   └── GameSaveManager.Infrastructure   # 文件系统、SQLite、HTTP、凭据及 Windows 实现
├── tests
│   └── GameSaveManager.Verification     # 不依赖额外测试框架的基础边界验证
├── scripts                              # Windows 发布与安装脚本
├── installer                            # Inno Setup 安装器定义
├── docs/db                              # 服务端相关数据库脚本
├── GameSaveManager.sln
└── TODO.md
```

## 架构边界

### `GameSaveManager.Domain`

保存不可变领域模型，不依赖 WPF、SQLite、HTTP 或 Windows API。

### `GameSaveManager.Application`

编排 Manifest 构建、云端同步、安全恢复、快照导出和启动流程。该层依赖抽象接口，不直接处理具体文件系统、数据库或凭据实现。

### `GameSaveManager.Infrastructure`

提供 Windows 文件系统、SHA-256、SQLite、HTTP API、Windows Credential Manager、游戏发现、进程监控和开机启动等实现。

### `GameSaveManager.App`

负责 WPF 界面、ViewModel、命令绑定和依赖组装，不承载核心同步规则。

## 快照原则

`FileSystemWatcher` 只负责设置 dirty 标记，不能作为快照事实来源。真正创建快照时必须重新扫描完整目录：

```text
完整扫描存档目录
    ↓
使用 size + LastWriteTimeUtc 查询 Hash Cache
    ↓
缓存失效时流式计算 SHA-256
    ↓
再次检查文件稳定性
    ↓
构建完整、不可变的 Manifest
```

SHA-256 是客户端和服务端内容对象的统一身份。服务端已有 HEAD 且 Manifest 完全一致时，本次提交应为幂等 no-op：不创建重复快照、不增加内容对象引用计数，也不推进 HEAD。

## 同步流程

```text
注册或登录
    ↓
设备 Token 按服务端隔离写入 Windows Credential Manager
    ↓
读取或创建云端逻辑游戏
    ↓
使用 serverKey + gameId 读取本地 sync_state
    ↓
读取远端 HEAD
    ↓
localHead == remoteHead ?
    ├─ 否：阻断普通上传，进入冲突或恢复流程
    └─ 是：继续
          ↓
      完整构建 Manifest
          ↓
      检查缺失内容对象
          ↓
      只上传缺失对象
          ↓
      提交 Snapshot Manifest
          ↓
      服务端以 CAS 推进 HEAD
          ↓
      本地 SQLite 更新 sync_state
```

多设备 HEAD 不一致时，用户可以恢复云端版本，或显式选择“保留本机版本”。保留本机版本会以当前远端 HEAD 为父版本提交新的 Manifest，原有版本继续保留在不可变时间线中。

## 安全恢复

恢复不会先删除真实存档目录。客户端必须先下载并验证全部对象，然后通过同盘目录切换完成替换：

```text
读取快照 Manifest
    ↓
下载对象到 .partial 临时文件
    ↓
校验文件大小与 SHA-256
    ↓
原子写入本地内容缓存
    ↓
构建同盘 staging 目录并再次校验
    ↓
将原存档目录改名为安全备份
    ↓
将 staging 目录改名为真实存档目录
    ↓
执行最终 SHA-256 校验
```

恢复日志保存在 `%LOCALAPPDATA%\GameSaveManager\restore\{transactionId}\journal.json`。应用重启后只会在状态明确且可证明安全的场景自动回滚；其他异常现场保留并提示用户处理，禁止猜测或静默覆盖数据。

设备 Token 只发送给 GameSave 服务端，不转发给 MinIO。对象下载使用服务端签发的短时预签名地址。

## 服务端地址与凭据隔离

服务端地址由统一入口规范化：

- 仅接受绝对 `http` 或 `https` URL。
- 远程服务端必须使用 HTTPS。
- HTTP 只允许 `localhost`、`127.0.0.1` 等回环地址。
- 基础地址禁止 Query 和 Fragment。
- scheme、host 统一小写，基础 path 保留原始大小写。
- 规范化结果经过 SHA-256 生成稳定 `serverKey`。

`serverKey` 同时用于 Windows Credential Manager 中的设备 Token target 和 SQLite 中的同步状态作用域，因此切换服务端不会复用其他服务端的 Token 或 HEAD。

## 本地数据

默认数据目录：

```text
%LOCALAPPDATA%\GameSaveManager
└── data
    └── gamesave.db
```

SQLite 主要保存：

- `file_hash_cache`：文件 SHA-256 缓存。
- `sync_state`：以 `server_key + game_id` 为作用域保存最后一次成功同步确认的云端 HEAD。
- `client_setting`：稳定设备 ID 等非敏感客户端设置。
- `local_game_profile`：按 `serverKey + gameId` 保存存档目录、进程名和自动同步配置。
- `schema_version`：本地数据库版本和迁移状态。

设备 Token 不写入 SQLite，统一保存到 Windows Credential Manager。

## 环境要求

- Windows 10/11。
- .NET 10 SDK。
- 可访问 GameSave CMS 服务端。
- 远程服务端启用 HTTPS。
- 服务端已执行 `docs/db/file_system.sql` 和 `docs/db/game_save.sql`。
- MinIO 中存在 `game-save-private` Bucket，或由部署流程负责创建。

## 构建和验证

在仓库根目录执行：

```powershell
dotnet restore .\GameSaveManager.sln
dotnet build .\GameSaveManager.sln -c Debug
dotnet run --project .\tests\GameSaveManager.Verification\GameSaveManager.Verification.csproj -c Debug --no-build
```

当前基础验证覆盖：

- localhost HTTP 允许、远程 HTTP 拒绝。
- Query 和 Fragment 服务端地址拒绝。
- scheme/host 大小写规范化和基础 path 大小写隔离。
- 旧 `sync_state` schema 迁移。
- 相同 `gameId` 在不同 `serverKey` 下的 HEAD 隔离。

真实 CMS、对象存储、多设备冲突和异常恢复仍需按 [TODO.md](TODO.md) 执行集成验证。

## Windows 发布

### 生成发布目录

默认生成 `win-x64` 自包含发布物，目标电脑不需要预装 .NET Runtime：

```powershell
.\scripts\publish-windows.ps1
```

生成依赖 .NET 10 Desktop Runtime 的小体积发布物：

```powershell
.\scripts\publish-windows.ps1 -DeploymentMode FrameworkDependent
```

默认输出目录：

- 自包含：`artifacts\publish\win-x64`
- Framework-dependent：`artifacts\publish\win-x64-framework-dependent`

发布脚本暂未启用 trimming。WPF、SQLite 原生库和系统凭据调用依赖动态加载，应先保证可用性和可诊断性，再进行专项裁剪验证。

### 生成安装包

安装 Inno Setup 6 后执行：

```powershell
.\scripts\build-installer.ps1 -Version 0.1.0
```

安装包输出到 `artifacts\installer`。默认按当前 Windows 用户安装到 `%LOCALAPPDATA%\Programs\GameSaveManager`，不需要管理员权限。

### 当前发布体积

基于 `win-x64`、Release、单文件配置的本机验证结果：

| 模式 | EXE 大小 | 适用场景 |
| --- | ---: | --- |
| `SelfContained` | 约 135.4 MiB | 面向普通用户，无需预装 Runtime |
| `FrameworkDependent` | 约 2.5 MiB | 内部分发或已预装 .NET 10 Desktop Runtime |

正式公开分发前仍需接入代码签名、时间戳服务、HTTPS 更新源、签名版本清单和回滚策略。

## 自动构建

`.github/workflows/build.yml` 使用 Windows Runner 和 .NET 10：

```text
windows-latest
    ↓
配置 .NET 10 SDK
    ↓
dotnet restore
    ↓
dotnet build -c Debug --no-restore
    ↓
GameSaveManager.Verification
    ↓
上传 restore/build/verification 日志 artifact
```

## 文档与代码规范

项目 C# XML 注释和项目文档统一使用中文。类名、接口名、HTTP 字段、数据库字段和稳定业务错误码保留英文，因为它们属于代码或协议契约。

## 后续任务

未完成的验证、发布基础设施和工程优化统一维护在 [TODO.md](TODO.md)。
