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
