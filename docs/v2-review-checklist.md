# GameSave Manager V2 评审检查清单

- [ ] 在 Windows + .NET 10 SDK 执行 `dotnet build .\GameSaveManager.V2.sln -c Debug`。
- [ ] 扫描空目录、嵌套目录和包含大量小文件的存档目录。
- [ ] Hash 期间修改文件，确认 Manifest 安全失败而不是生成半一致快照。
- [ ] 确认 Reparse Point 不会被递归遍历。
- [ ] 重启客户端后确认 SQLite Hash Cache 仍能命中。
- [ ] 确认 SQLite 不包含设备 Token 明文。
- [ ] 确认设备 Token 存在 Windows Credential Manager。
- [ ] 首次同步远端 HEAD 为空时可以创建 Snapshot。
- [ ] 本地 `sync_state` HEAD 与远端 HEAD 不一致时必须阻断上传。
- [ ] 服务端返回 409 `SYNC_CONFLICT` 时客户端保留稳定错误码。
- [ ] 上传同一 SHA-256 内容的多个路径时只上传一个缺失内容对象。
