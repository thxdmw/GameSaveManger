using System.Text.Json;
using GameSaveManager.Application;
using GameSaveManager.Application.Api;
using GameSaveManager.Infrastructure.Api;

namespace GameSaveManager.Verification;

internal static class ProtocolContractVerification
{
    public static void Verify()
    {
        Ensure(GameSaveProtocolLimits.MaximumManifestFiles == 5000, "Manifest 文件上限必须为 5000。");
        Ensure(GameSaveProtocolLimits.MaximumSnapshotListLimit == 200, "快照列表上限必须为 200。");
        Ensure(GameSaveProtocolLimits.RelativePathMaxLength == 1024, "相对路径上限必须为 1024。");
        Ensure(GameSaveProtocolLimits.DescriptionMaxLength == 500, "描述上限必须为 500。");
        Ensure(GameSaveProtocolLimits.MaximumSnapshotRoots == 32, "快照根目录上限必须为 32。");
        Ensure(GameSaveProtocolLimits.RootIdMaxLength == 64, "根目录 ID 上限必须为 64。");
        Ensure(GameSaveProtocolLimits.PathTemplateMaxLength == 1024, "路径模板上限必须为 1024。");
        Ensure(GameSaveProtocolLimits.MaximumPatternsPerRoot == 64, "单类扫描规则上限必须为 64。");
        Ensure(GameSaveProtocolLimits.PatternMaxLength == 256, "扫描规则长度上限必须为 256。");

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new CmsDateTimeOffsetConverter());
        CloudSnapshotSummary? summary = JsonSerializer.Deserialize<CloudSnapshotSummary>(
            """
            {
              "snapshotId": "snapshot-1",
              "parentSnapshotId": null,
              "deviceId": "device-1",
              "triggerType": "MANUAL",
              "description": "fixture",
              "fileCount": 2,
              "logicalSize": 128,
              "changedFileCount": 2,
              "createTime": "2026-07-16T04:30:00Z"
            }
            """,
            options);
        Ensure(summary is not null && summary.CreateTime.Offset == TimeSpan.Zero,
            "UTC 快照样例必须可反序列化且保留 UTC 偏移。");

        ClientOperationError rateLimited = ClientOperationError.FromException(
            new GameSaveApiException(429, "LOGIN_RATE_LIMITED", "rate limited",
                TimeSpan.FromSeconds(30), "request-fixture"));
        Ensure(rateLimited.CanRetry
               && rateLimited.SuggestedRetryDelay == TimeSpan.FromSeconds(30)
               && rateLimited.RequestId == "request-fixture",
            "429 样例必须保留 Retry-After 和 X-Request-ID。");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
