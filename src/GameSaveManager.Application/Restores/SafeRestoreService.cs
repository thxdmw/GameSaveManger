using System.Text.Json;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Files;
using GameSaveManager.Application.Sync;

namespace GameSaveManager.Application.Restores;

/// <summary>
/// 用于恢复游戏存档的事务编排：对象先校验进缓存，再构建临时目录；
/// 原存档目录仅在临时目录完整校验后才会移动为安全备份，绝不先删除再复制。
/// </summary>
public sealed class SafeRestoreService(
    IGameSaveApiClient apiClient,
    ContentObjectCache objectCache,
    IFileHashService fileHashService,
    ILocalSyncStateStore localSyncStateStore)
{
    private static readonly JsonSerializerOptions JournalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string RestoreRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameSaveManager",
        "restore");

    public async Task<RestoreResult> RestoreAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string snapshotId,
        string saveDirectory,
        CancellationToken cancellationToken)
    {
        CloudSnapshotManifest manifest = await apiClient.GetSnapshotAsync(
            server, deviceToken, gameId, snapshotId, cancellationToken);
        ValidateManifest(manifest);
        CloudHead remoteHead = await apiClient.GetHeadAsync(server, deviceToken, gameId, cancellationToken);

        string target = Path.GetFullPath(saveDirectory);
        if (File.Exists(target))
        {
            throw new IOException($"存档目录路径被同名文件占用: {target}");
        }
        string parent = Path.GetDirectoryName(target)
            ?? throw new IOException("存档目录必须具有父目录");
        Directory.CreateDirectory(parent);

        string transactionId = Guid.NewGuid().ToString("N");
        string transactionDirectory = Path.Combine(RestoreRoot, transactionId);
        string staging = Path.Combine(parent, $".{Path.GetFileName(target)}.gamesave-staging-{transactionId}");
        string safetyBackup = Path.Combine(parent, $".{Path.GetFileName(target)}.gamesave-safety-{transactionId}");
        string journalPath = Path.Combine(transactionDirectory, "journal.json");
        Directory.CreateDirectory(transactionDirectory);

        var journal = new RestoreJournal(
            transactionId,
            RestoreJournalState.Prepared,
            target,
            staging,
            Directory.Exists(target) ? safetyBackup : null,
            manifest.SnapshotId,
            DateTimeOffset.UtcNow);
        await WriteJournalAsync(journalPath, journal, cancellationToken);

        try
        {
            await BuildAndVerifyStagingAsync(
                server, deviceToken, manifest, staging, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(target))
            {
                Directory.Move(target, safetyBackup);
                journal = journal with
                {
                    State = RestoreJournalState.OriginalMoved,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await WriteJournalAsync(journalPath, journal, CancellationToken.None);
            }

            Directory.Move(staging, target);
            journal = journal with
            {
                State = RestoreJournalState.Applied,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await WriteJournalAsync(journalPath, journal, CancellationToken.None);

            await VerifyDirectoryAsync(target, manifest.Files, CancellationToken.None);
            journal = journal with
            {
                State = RestoreJournalState.Completed,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await WriteJournalAsync(journalPath, journal, CancellationToken.None);

            await localSyncStateStore.SaveAsync(
                new LocalSyncState(GameSaveServerIdentity.CreateStableKey(server), gameId, remoteHead.HeadSnapshotId, remoteHead.Version),
                CancellationToken.None);

            // 完成后仅删除事务日志；原目录的安全备份刻意保留，避免恢复后立即失去回滚点。
            Directory.Delete(transactionDirectory, recursive: true);
            return new RestoreResult(manifest.SnapshotId, target, journal.SafetyBackupDirectory);
        }
        catch
        {
            // 发生故障时不删除安全备份或日志；下次启动可据此回滚或提示用户处理。
            throw;
        }
    }

    /// <summary>
    /// 应用启动时调用。只在“原目录已移走且目标目录不存在”这一确定场景自动回滚；
    /// 其他场景保留现场，避免用猜测覆盖可能已恢复成功的用户数据。
    /// </summary>
    public async Task<IReadOnlyList<string>> RecoverInterruptedRestoresAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(RestoreRoot))
        {
            return Array.Empty<string>();
        }

        var messages = new List<string>();
        foreach (string journalPath in Directory.EnumerateFiles(RestoreRoot, "journal.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreJournal? journal = await ReadJournalAsync(journalPath, cancellationToken);
            if (journal is null || journal.State is RestoreJournalState.Completed)
            {
                continue;
            }

            bool targetExists = Directory.Exists(journal.SaveDirectory);
            bool backupExists = !string.IsNullOrWhiteSpace(journal.SafetyBackupDirectory)
                                && Directory.Exists(journal.SafetyBackupDirectory);
            if (!targetExists && backupExists && journal.State is RestoreJournalState.Prepared or RestoreJournalState.OriginalMoved)
            {
                Directory.Move(journal.SafetyBackupDirectory!, journal.SaveDirectory);
                messages.Add($"已从安全备份回滚未完成的存档恢复: {journal.SaveDirectory}");
            }
            else
            {
                messages.Add($"发现未完成的存档恢复，已保留现场等待处理: {journal.SaveDirectory}");
            }
        }
        return messages;
    }

    private async Task BuildAndVerifyStagingAsync(
        Uri server,
        string deviceToken,
        CloudSnapshotManifest manifest,
        string staging,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(staging))
        {
            throw new IOException($"恢复临时目录已存在，拒绝覆盖: {staging}");
        }
        Directory.CreateDirectory(staging);

        foreach (CloudSnapshotFile file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string cached = await objectCache.GetOrDownloadAsync(
                file,
                (temporary, token) => apiClient.DownloadObjectAsync(
                    server, deviceToken, file.ObjectId, temporary, token),
                cancellationToken);
            string destination = ResolveContainedPath(staging, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(cached, destination, overwrite: false);
        }
        await VerifyDirectoryAsync(staging, manifest.Files, cancellationToken);
    }

    private async Task VerifyDirectoryAsync(
        string root,
        IReadOnlyList<CloudSnapshotFile> files,
        CancellationToken cancellationToken)
    {
        foreach (CloudSnapshotFile file in files)
        {
            string path = ResolveContainedPath(root, file.RelativePath);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Size)
            {
                throw new InvalidDataException($"恢复文件大小校验失败: {file.RelativePath}");
            }
            string hash = await fileHashService.ComputeSha256Async(path, cancellationToken);
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"恢复文件 SHA-256 校验失败: {file.RelativePath}");
            }
        }
    }

    private static void ValidateManifest(CloudSnapshotManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.SnapshotId) || manifest.Files is null)
        {
            throw new InvalidDataException("云端快照 Manifest 不完整");
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CloudSnapshotFile file in manifest.Files)
        {
            if (!paths.Add(file.RelativePath))
            {
                throw new InvalidDataException($"云端快照包含重复路径: {file.RelativePath}");
            }
            _ = ResolveContainedPath(Path.GetTempPath(), file.RelativePath);
        }
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("快照文件路径必须是非空相对路径");
        }
        string rootPath = Path.GetFullPath(root);
        string candidate = Path.GetFullPath(Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string relative = Path.GetRelativePath(rootPath, candidate);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"快照文件路径越过存档目录边界: {relativePath}");
        }
        return candidate;
    }

    private static async Task WriteJournalAsync(
        string path,
        RestoreJournal journal,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        string json = JsonSerializer.Serialize(journal, JournalJsonOptions);
        await File.WriteAllTextAsync(temporary, json, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private static async Task<RestoreJournal?> ReadJournalAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<RestoreJournal>(json, JournalJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}