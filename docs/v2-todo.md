# GameSave Manager V2 后续任务

## 高优先级

- 游戏进程启动/退出监听，并增加周期性进程状态校准。
- `FileSystemWatcher` dirty 标记；Watcher 只能触发扫描，不能作为 Snapshot 事实来源。
- 云端 Snapshot 时间线和 Manifest 下载。
- 安全恢复 Journal、staging 校验和恢复前 safety snapshot。
- 多设备冲突选择界面：保留本机、使用云端、两个版本都保留。
- 为本机游戏安装信息增加 SQLite 持久化。

## 游戏发现

- Steam LibraryFolders 和 AppManifest 扫描。
- Steam AppId 与云端逻辑游戏关联。
- GOG/Epic 发现适配器。
- 存档路径规则库和用户手工覆盖。

## 客户端工程

- 用 CommunityToolkit.Mvvm 替换当前最小 `INotifyPropertyChanged`/`AsyncCommand` 样板代码。
- 增加 Serilog 文件日志和敏感字段脱敏。
- 增加 HttpClient 重试策略；上传只对可安全重试场景启用。
- 增加 SQLite schema version 和迁移机制。
- 增加单元测试与 Windows 集成测试。

## 发布

- MSIX 或安装器方案。
- 自动更新。
- 代码签名。
- self-contained、framework-dependent、trimming 的发布体积对比测试。

## 代码规范

V2 新增 C# XML 注释和 V2 专用文档统一使用中文；代码标识符、协议字段和稳定错误码继续使用英文。

## 2026-07-12 实施状态

已完成：

- Steam、Epic、GOG 本机游戏发现和进程名预填。
- 存档目录与自动快照配置按服务端、游戏隔离持久化。
- 设备列表、其他设备撤销和当前设备保护。
- 云端时间线、Manifest 下载、安全恢复、冲突保留本机版本。
- 历史快照删除确认和当前 HEAD 删除保护。

下一阶段：

- 用户存储配额原子预占、释放和容量统计。
- 快照保留策略与后台清理调度。
- Serilog 脱敏日志、HTTP 安全重试和 SQLite 版本化迁移。
- Windows 安装包、签名、自动更新与发布体积验证。
## 2026-07-12 配额进展

已完成服务端原子配额预占、失败补偿、零引用对象容量释放和客户端容量展示。后续重点调整为快照保留策略、日志与安全重试、SQLite 版本化迁移，以及 Windows 安装发布。
## 2026-07-12 保留策略进展

已完成每游戏保留数量、保留天数、默认关闭开关、当前 HEAD 保护、手动立即清理和后台自动清理的客户端配置。后续重点为日志脱敏、安全重试、SQLite 版本化迁移和 Windows 安装发布。