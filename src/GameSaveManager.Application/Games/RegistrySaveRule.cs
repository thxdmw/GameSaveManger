namespace GameSaveManager.Application.Games;

/// <summary>一个经用户确认的 HKCU 注册表存档根；规则 ID 同时用于云端 JSON 文件名。</summary>
public sealed record RegistrySaveRule(string RuleId, string KeyPath, bool UserConfirmed);
