# GameSave Manager 架构说明

## 项目边界

- `GameSaveManager.App`：WPF 界面、ViewModel 和应用组合根。
- `GameSaveManager.Domain`：游戏、快照等不可变领域模型。
- `GameSaveManager.Infrastructure`：Windows 文件系统、SHA-256、SQLite、HTTP API 和系统凭据实现。
- `tests/GameSaveManager.Verification`：无额外测试框架依赖的基础边界验证程序，只用于 CI，不进入正式客户端发布物。

客户端代码位于 `src/GameSaveManager.*`，使用 .NET 10 与 WPF 构建。

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

项目 C# XML 注释和 项目文档统一使用中文。类名、接口名、HTTP 字段、数据库字段和稳定业务错误码保留英文标识，因为它们属于代码/协议契约。

## 自动构建与验证边界

`.github/workflows/build.yml` 使用 Windows Runner 和 .NET 10：

1. 还原依赖。
2. 编译 解决方案。
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


## 安全恢复链路

恢复不会执行“删除真实存档后复制备份”。客户端必须先读取云端快照 Manifest，按每个对象的 `objectId` 向 GameSave 服务申请短时预签名下载地址；设备 Token 只发送给 GameSave，绝不转发给 MinIO。

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
最终 SHA-256 校验
```

恢复日志保存在 `%LOCALAPPDATA%\GameSaveManager\restore\{transactionId}\journal.json`。应用重启时，仅当“原目录已移走、目标目录不存在且安全备份存在”这一确定场景才自动回滚；其余异常现场保留并提示用户，禁止猜测或静默覆盖数据。恢复成功后安全备份保留在原存档目录旁，供用户自行核验和清理。
## 时间线、冲突与自动快照

客户端通过 `GET /api/game-save/v1/games/{gameId}/snapshots?limit=100` 读取按创建时间倒序排列的时间线。选择任意版本即可执行安全恢复；该操作会先下载和校验所有对象，再使用同盘 staging 目录替换真实存档，并保留原目录安全备份。

多设备 HEAD 不一致时，普通同步会被阻止。用户可以：

- 从时间线恢复云端版本；
- 显式点击“冲突时保留本机版本”。客户端会以当前云端 HEAD 为父版本提交本机 Manifest，两个版本都会留在不可变时间线中。

自动快照由两个信号共同决定：`FileSystemWatcher` 仅设置 dirty 标记，Windows 进程轮询确认游戏退出后才调用同步服务并以 `GAME_EXIT` 创建快照。这样不会因重复、丢失或乱序的目录事件创建半一致版本。

## 本机游戏发现

Windows 客户端支持扫描：

- Steam 的 `libraryfolders.vdf` 和 `appmanifest_*.acf`；
- Epic Launcher 的 `.item` 安装清单；
- GOG 的 Windows 安装注册表。

扫描结果只用于预填云端游戏名称、提供方、提供方游戏 ID 和进程名。游戏存档目录始终要求用户手动确认，避免因猜测路径造成错误恢复。
## 本机配置、设备与历史快照

本机游戏配置按 `serverKey + gameId` 写入 SQLite `local_game_profile`，保存用户确认过的存档目录、进程名和自动快照开关。切换服务端或云端游戏不会串用路径；登录后可恢复对应配置，并仅在目录仍有效时恢复自动监控。

设备管理通过 GameSave API 读取设备列表并撤销其他设备。客户端不会展示或持久化服务端保存的 Token Hash；撤销成功后目标设备原有 Bearer Token 立即失效，当前设备必须在服务端重新认证流程中处理，避免误操作导致当前会话中断。

时间线允许删除非当前 HEAD 的历史快照。界面在删除前显示明确确认；服务端拒绝删除当前 HEAD。历史快照删除后，客户端重新加载时间线，云端对不再被任何快照引用的内容对象执行引用释放和延迟清理。
## 存储配额显示

客户端通过设备 Token 调用 `GET /api/game-save/v1/account/quota`，展示按去重内容对象计算的已用、总计和剩余物理容量。认证成功后自动加载；创建、同步、冲突提交或删除历史快照后自动刷新，也允许用户手动刷新。客户端只展示服务端权威计数，不在本地根据 Manifest 推算配额。
## 快照保留策略设置

客户端按云端游戏读取和保存保留策略，可配置最多保留 1 到 500 个快照、保留 0 到 3650 天，并可立即执行一次清理。自动清理默认关闭；界面明确提示当前 HEAD 始终保留。立即清理后客户端重新加载时间线和存储配额。