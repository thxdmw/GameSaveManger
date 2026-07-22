using GameSaveManager.Application.Api;

namespace GameSaveManager.Verification;

internal static class CloudApiResponseValidationVerification
{
    public static void Verify()
    {
        CloudApiResponseValidator.ValidateGames(
        [
            new CloudGame("game-1", "LIMBO", "CUSTOM", null),
            new CloudGame("game-2", "INSIDE", "STEAM", "304430")
        ]);
        ExpectInvalid(
            () => CloudApiResponseValidator.ValidateGames(
            [
                new CloudGame("game-1", "LIMBO", "CUSTOM", null),
                new CloudGame("game-1", "INSIDE", "CUSTOM", null)
            ]),
            "重复游戏 ID 应被拒绝");
        ExpectInvalid(
            () => CloudApiResponseValidator.ValidateCreatedGame(
                new CloudGame("game-2", "另一个游戏", "CUSTOM", null), "LIMBO", "CUSTOM"),
            "创建响应与请求游戏不一致时应被拒绝");

        CloudApiResponseValidator.ValidateRetentionPolicy(
            new CloudRetentionPolicy("game-1", true, 50, 30), "game-1");
        ExpectInvalid(
            () => CloudApiResponseValidator.ValidateRetentionPolicy(
                new CloudRetentionPolicy("game-2", true, 50, 30), "game-1"),
            "错绑游戏的保留策略应被拒绝");

        CloudApiResponseValidator.ValidateQuota(new CloudQuota(100, 40, 60));
        ExpectInvalid(
            () => CloudApiResponseValidator.ValidateQuota(new CloudQuota(100, 40, 70)),
            "字段不一致的配额应被拒绝");

        CloudApiResponseValidator.ValidateDevices(
        [
            new CloudDevice("device-1", "电脑一", DateTimeOffset.UtcNow, true, DateTimeOffset.UtcNow)
        ]);
        ExpectInvalid(
            () => CloudApiResponseValidator.ValidateDevices(
            [
                new CloudDevice("device-1", "电脑一", null, true, null),
                new CloudDevice("device-1", "电脑二", null, true, null)
            ]),
            "重复设备 ID 应被拒绝");

        CloudSnapshotSummary snapshot = CreateSnapshot("snapshot-1");
        CloudApiResponseValidator.ValidateSnapshots([snapshot], 100);
        ExpectInvalid(
            () => CloudApiResponseValidator.ValidateSnapshots([snapshot, snapshot], 100),
            "重复快照 ID 应被拒绝");

        CloudSnapshotManifest manifest = CreateManifest("snapshot-1", "root/save.dat");
        CloudApiResponseValidator.ValidateManifest(manifest, "game-1", "snapshot-1");
        ExpectInvalid(
            () => CloudApiResponseValidator.ValidateManifest(
                CreateManifest("snapshot-1", "root/folder/../save.dat"), "game-1", "snapshot-1"),
            "包含父目录跳转的快照路径应被拒绝");
        ExpectInvalid(
            () => CloudApiResponseValidator.ValidateManifest(
                CreateManifest("snapshot-1", "unknown/save.dat"), "game-1", "snapshot-1"),
            "引用未声明根目录的快照路径应被拒绝");
        ExpectInvalid(
            () => CloudApiResponseValidator.ValidateManifest(manifest, "game-2", "snapshot-1"),
            "完整快照与请求游戏不一致时应被拒绝");
    }

    private static CloudSnapshotSummary CreateSnapshot(string snapshotId) => new(
        snapshotId,
        null,
        "device-1",
        "MANUAL",
        null,
        1,
        16,
        1,
        DateTimeOffset.UtcNow,
        [new CloudSnapshotRoot("root", "FILE", "%DOCUMENTS%\\My Games\\LIMBO", "MANUAL", 100, [], [])]);

    private static CloudSnapshotManifest CreateManifest(string snapshotId, string relativePath) => new(
        snapshotId,
        "game-1",
        "device-1",
        null,
        "MANUAL",
        null,
        DateTimeOffset.UtcNow,
        [new CloudSnapshotRoot("root", "FILE", "%DOCUMENTS%\\My Games\\LIMBO", "MANUAL", 100, [], [])],
        [new CloudSnapshotFile(relativePath, new string('a', 64), new string('b', 64), 16)]);

    private static void ExpectInvalid(Action action, string message)
    {
        try
        {
            action();
            throw new InvalidOperationException(message);
        }
        catch (InvalidDataException) { }
    }
}
