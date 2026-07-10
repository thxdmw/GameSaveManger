# GameSave Manager V2 PR 摘要

本 PR 在旧版 .NET Framework 4.8 + WinForms 项目旁新增 .NET 10 + WPF V2 客户端，旧代码保持不变。

当前已实现：

- Domain / Application / Infrastructure / App 四项目边界。
- 完整存档目录扫描。
- SHA-256 流式 Hash 和文件稳定性检查。
- SQLite 持久化 Hash Cache。
- 本地同步 HEAD 状态。
- Windows Credential Manager 设备 Token 存储。
- 注册、登录和云端游戏库 API Client。
- 缺失内容对象检查和流式上传。
- 不可变 Snapshot Manifest 提交。
- 本地 HEAD 与远端 HEAD 不一致时阻断上传。
- WPF 同步闭环验证界面。
- Windows + .NET 10 GitHub Actions 自动构建。

所有 V2 新增代码注释和项目文档统一使用中文。

PR 在 Windows/.NET 10 自动构建通过前保持 Draft。
