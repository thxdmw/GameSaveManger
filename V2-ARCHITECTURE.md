# GameSave Manager V2 架构说明

## 项目边界

- `GameSaveManager.App`：WPF 界面、ViewModel 和应用组合根。
- `GameSaveManager.Application`：用例编排、快照 Manifest、同步流程和基础设施契约。
- `GameSaveManager.Domain`：游戏、快照等不可变领域模型。
- `GameSaveManager.Infrastructure`：Windows 文件系统、SHA-256、SQLite、HTTP API 和系统凭据实现。

V2 迁移期间旧版 WinForms 保持原样，新代码全部位于 `src/GameSaveManager.*`。

## 快照原则

`FileSystemWatcher` 以后只负责设置 dirty 标记，不作为事实来源。

真正创建 Snapshot 时必须：

```text
完整扫描存档目录
    ↓
size + LastWriteTimeUtc 查询 Hash Cache
    ↓
缓存失效才流式计算 SHA-256
    ↓
再次检查文件稳定性
    ↓
构建完整不可变 Manifest
```

SHA-256 是客户端、CMS `module.gamesave` 和 `module.file` 统一的内容身份。

## 当前同步闭环

```text
注册/登录
    ↓
设备 Token 写入 Windows Credential Manager
    ↓
读取或创建云端逻辑游戏
    ↓
读取本地 sync_state
    ↓
读取远端 HEAD
    ↓
localHead == remoteHead ?
    ├─ 否：阻断上传，进入冲突/恢复流程
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
服务端 CAS 推进 HEAD
          ↓
本地 SQLite 更新 sync_state
```

## 本地持久化

默认目录：

```text
%LOCALAPPDATA%\GameSaveManager
└── data
    └── gamesave.db
```

SQLite 当前保存：

- `file_hash_cache`：文件 SHA-256 缓存。
- `sync_state`：每个游戏最后一次成功同步确认的云端 HEAD。
- `client_setting`：非敏感客户端设置，目前保存稳定 `deviceId`。

设备 Token 不进入 SQLite，统一保存到 Windows Credential Manager。

## 尚未实现

- Steam/GOG/Epic 自动发现。
- 游戏进程开始/退出监听。
- `FileSystemWatcher` dirty 标记。
- Snapshot 时间线。
- 云端 Manifest 下载。
- 安全恢复 Journal 和 staging 目录。
- 多设备冲突选择界面。
