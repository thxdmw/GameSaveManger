# GameSave Manager V2 架构说明

## 项目边界

- `GameSaveManager.App`：WPF 界面、ViewModel 和应用组合根。
- `GameSaveManager.Application`：用例编排、快照 Manifest、同步流程和基础设施契约。
- `GameSaveManager.Domain`：游戏、快照等不可变领域模型。
- `GameSaveManager.Infrastructure`：Windows 文件系统、SHA-256、SQLite、HTTP API 和系统凭据实现。
- `tests/GameSaveManager.Verification`：无额外测试框架依赖的基础边界验证程序，只用于 CI，不进入正式客户端发布物。

V2 迁移期间旧版 WinForms 保持原样，正式客户端新代码全部位于 `src/GameSaveManager.*`。

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

服务端已有 HEAD 且 Manifest 与当前版本完全一致时，本次提交为幂等 no-op：不创建 Snapshot、不插入 `snapshot_file`、不增加内容对象引用计数，也不推进 HEAD。

## 当前同步闭环

```text
注册/登录
    ↓
设备 Token 按服务端隔离写入 Windows Credential Manager
    ↓
读取或创建云端逻辑游戏
    ↓
serverKey + gameId 读取本地 sync_state
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
Manifest 未变化？
    ├─ 是：服务端返回当前 HEAD，no-op
    └─ 否：服务端 CAS 推进 HEAD
          ↓
本地 SQLite 更新 sync_state
```

## 服务端地址与凭据作用域

`GameSaveServerIdentity` 是客户端唯一的服务端地址规范化入口：

- 仅接受绝对 `http/https` URL。
- 远程服务端必须使用 HTTPS。
- HTTP 只允许 localhost/回环地址。
- 基础地址禁止 Query 和 Fragment。
- scheme/host 统一小写。
- 基础 path 保留原始大小写。
- 规范化结果 SHA-256 后作为 `serverKey`。

同一个 `serverKey` 同时用于：

- Windows Credential Manager 的设备 Token target。
- SQLite `sync_state` 服务端作用域。

因此切换服务端不会复用其他服务签发的 Bearer Token，也不会复用其他服务端的本地 HEAD。

## 本地持久化

默认目录：

```text
%LOCALAPPDATA%\GameSaveManager
└── data
    └── gamesave.db
```

SQLite 当前保存：

- `file_hash_cache`：文件 SHA-256 缓存。
- `sync_state`：以 `server_key + game_id` 为复合主键，保存最后一次成功同步确认的云端 HEAD。
- `client_setting`：非敏感客户端设置，目前保存稳定 `deviceId`。

第一版实验 `sync_state` 只有 `game_id` 时，启动会检测缺少 `server_key` 并重建该表。旧 HEAD 无法可靠判断属于哪个服务端，因此禁止猜测迁移。

设备 Token 不进入 SQLite，统一保存到 Windows Credential Manager。注册或登录完成后，ViewModel 和 PasswordBox 中的密码明文都会主动清空。

## 注释与文档规范

V2 新增 C# XML 注释和 V2 专用文档统一使用中文。类名、接口名、HTTP 字段、数据库字段和稳定业务错误码保留英文标识，因为它们属于代码/协议契约。

## 自动构建与验证边界

`.github/workflows/v2-build.yml` 使用 Windows Runner 和 .NET 10：

1. 还原依赖。
2. 编译 V2 Solution。
3. 运行 `GameSaveManager.Verification`。
4. 无论成功失败都保存 restore/build/verification 日志 artifact。

当前验证程序覆盖：

- localhost HTTP 允许。
- 远程 HTTP 拒绝。
- Query/Fragment 服务端地址拒绝。
- scheme/host 大小写不影响服务端稳定标识。
- 基础 path 大小写保持隔离。
- 旧 `sync_state` schema migration。
- 同一 `gameId` 在不同 `serverKey` 下 HEAD 相互隔离。

自动构建和基础边界验证通过后，仍需执行客户端到 CMS 的真实数据库/MinIO 同步集成测试和多设备冲突测试。

## 尚未实现

- Steam/GOG/Epic 自动发现。
- 游戏进程开始/退出监听。
- `FileSystemWatcher` dirty 标记。
- Snapshot 时间线。
- 云端 Manifest 下载。
- 本地对象缓存。
- 安全恢复 Journal 和 staging 目录。
- 多设备冲突选择界面。
