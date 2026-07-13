using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Infrastructure.Discovery;

/// <summary>只返回磁盘上已存在的常见 Windows 存档目录，避免错误地把猜测路径当成存档路径。</summary>
public sealed class WindowsSavePathSuggestionService : ISavePathSuggestionService
{
    public Task<IReadOnlyList<string>> SuggestAsync(string gameName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);
        cancellationToken.ThrowIfCancellationRequested();
        string safeName = gameName.Trim();
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string savedGames = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localLow = Path.Combine(local, "..", "LocalLow");
        string[] candidates =
        [
            Path.Combine(documents, "My Games", safeName),
            Path.Combine(documents, safeName),
            Path.Combine(savedGames, safeName),
            Path.Combine(local, safeName),
            Path.Combine(roaming, safeName),
            Path.Combine(localLow, safeName)
        ];
        IReadOnlyList<string> existing = candidates
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(existing);
    }
}