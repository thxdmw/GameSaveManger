namespace GameSaveManager.Application.Launching;

public interface IShortcutResolver
{
    Task<ShortcutResolution> ResolveAsync(string shortcutPath, CancellationToken cancellationToken);
}

public sealed record ShortcutResolution(
    bool Resolved,
    string ShortcutPath,
    string? TargetPath,
    string? Arguments,
    string? WorkingDirectory,
    string? IconLocation,
    string? FailureReason);
