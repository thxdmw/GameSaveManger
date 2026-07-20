# GameSave Manager 公开发布者证书

本目录只保存可以公开分发的自签代码签名证书和非敏感元数据，不保存私钥、PFX 或密码。

- 文件：`GameSaveManager-Publisher.cer`
- 主题：`CN=GameSave Manager Self-Signed Publisher`
- 算法：RSA 3072 / SHA-256
- 用途：代码签名
- 有效期至：2031-07-20
- 证书 SHA-256：`14768BC7D3CF2B1EB5BBE3228ADC8A3D35A1F923CB806B64147D7CFD3BCA8E35`
- SHA-1 指纹（仅用于本机证书库选择）：`C2299BA7748C11E2D4D8FA5080F38990B8FB3D05`

用户必须从项目官方仓库或 GitHub Release 获取证书，核对 SHA-256 后再运行 `scripts/Install-GameSaveManagerCertificate.ps1`。自签证书代表“用户明确相信项目维护者发布的此证书”，不代表第三方 CA 已经验证发布者身份，也不能保证消除 Windows SmartScreen 信誉提示。

发布私钥默认保存在仓库外：

```text
%USERPROFILE%\.gamesavemanager-secrets\GameSaveManager-Publisher.pfx
%USERPROFILE%\.gamesavemanager-secrets\GameSaveManager-Publisher.password.dpapi
```

这些文件不得复制到仓库、Release、Issue、日志或聊天记录。
