# GameSave Manager V2 评审检查清单

- [x] GitHub Actions `Windows .NET 10 构建` 通过。
- [x] CI 执行 `GameSaveManager.Verification` 基础边界验证通过。
- [x] 验证 localhost HTTP 允许、远程 HTTP 拒绝。
- [x] 验证服务端基础地址 Query/Fragment 被拒绝。
- [x] 验证 scheme/host 大小写不影响服务端 Key，基础 path 大小写保持隔离。
- [x] 验证旧 `sync_state` 会迁移为包含 `server_key` 的新结构。
- [x] 验证相同 `gameId` 在不同 `serverKey` 下 HEAD 相互隔离。
- [ ] 扫描空目录、嵌套目录和包含大量小文件的真实游戏存档目录。
- [ ] Hash 期间修改文件，确认 Manifest 安全失败而不是生成半一致快照。
- [ ] 确认 Reparse Point 不会被递归遍历。
- [ ] 重启客户端后确认 SQLite Hash Cache 仍能命中。
- [ ] 确认 SQLite 不包含设备 Token 明文。
- [ ] 确认设备 Token 存在 Windows Credential Manager，并按服务端地址隔离。
- [ ] 首次同步远端 HEAD 为空时可以创建 Snapshot。
- [ ] 第二次同步相同 Manifest 时服务端返回 `created=false`，客户端显示未创建重复版本。
- [ ] 本地 `sync_state` HEAD 与远端 HEAD 不一致时必须阻断上传。
- [ ] 服务端返回 409 `SYNC_CONFLICT` 时客户端保留稳定错误码。
- [ ] 上传同一 SHA-256 内容的多个路径时只上传一个缺失内容对象。
- [ ] 客户端到 CMS + MySQL + MinIO 真实同步闭环完成集成验证。

稳定协议字段和错误码使用英文标识；C# XML 注释和 V2 文档统一使用中文。
