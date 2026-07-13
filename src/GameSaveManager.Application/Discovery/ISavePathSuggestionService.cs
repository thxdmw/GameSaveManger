namespace GameSaveManager.Application.Discovery;

/// <summary>根据游戏名给出本机真实存在的常见存档目录候选；最终选择仍由用户确认。</summary>
public interface ISavePathSuggestionService
{
    Task<IReadOnlyList<string>> SuggestAsync(string gameName, CancellationToken cancellationToken);
}