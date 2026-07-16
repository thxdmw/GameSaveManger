using System.Text.Json;
using GameSaveManager.Application.Api;
using GameSaveManager.Infrastructure.Api;

internal static class CmsDateTimeOffsetVerification
{
    internal static void Verify()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new CmsDateTimeOffsetConverter());

        CloudDevice? device = JsonSerializer.Deserialize<CloudDevice>(
            "{\"deviceId\":\"device-1\",\"deviceName\":\"PC\",\"lastSeenTime\":\"2026-07-14 00:41:18\",\"active\":true,\"createTime\":1720888878000}",
            options);
        if (device?.LastSeenTime is null || device.CreateTime is null)
        {
            throw new InvalidOperationException("CMS 设备时间未能正确解析。");
        }
        DateTimeOffset expectedDeviceCreateTime = DateTimeOffset.FromUnixTimeMilliseconds(1720888878000);
        if (device.CreateTime.Value.UtcDateTime != expectedDeviceCreateTime.UtcDateTime
            || device.CreateTime.Value.Offset != TimeZoneInfo.Local.GetUtcOffset(device.CreateTime.Value.DateTime))
        {
            throw new InvalidOperationException("CMS Unix 时间戳未转换为系统本地时区。");
        }

        CloudSnapshotSummary? snapshot = JsonSerializer.Deserialize<CloudSnapshotSummary>(
            "{\"snapshotId\":\"snapshot-1\",\"parentSnapshotId\":null,\"deviceId\":\"device-1\",\"triggerType\":\"MANUAL\",\"description\":null,\"fileCount\":1,\"logicalSize\":12,\"changedFileCount\":1,\"createTime\":\"2026-07-14T00:41:18\"}",
            options);
        if (snapshot is null || snapshot.CreateTime.Year != 2026)
        {
            throw new InvalidOperationException("CMS 快照时间未能正确解析。");
        }

        string utcSnapshotJson = "{'snapshotId':'snapshot-2','parentSnapshotId':null,'deviceId':'device-1','triggerType':'MANUAL','description':null,'fileCount':1,'logicalSize':12,'changedFileCount':1,'createTime':'2026-07-14T00:41:18.000Z'}".Replace('\'', (char)34);
        CloudSnapshotSummary? utcSnapshot = JsonSerializer.Deserialize<CloudSnapshotSummary>(utcSnapshotJson, options);
        DateTimeOffset expectedSnapshotTime = DateTimeOffset.Parse("2026-07-14T00:41:18.000Z");
        if (utcSnapshot is null || utcSnapshot.CreateTime.UtcDateTime != expectedSnapshotTime.UtcDateTime
            || utcSnapshot.CreateTime.Offset != TimeZoneInfo.Local.GetUtcOffset(utcSnapshot.CreateTime.DateTime))
        {
            throw new InvalidOperationException("CMS UTC 快照时间未转换为系统本地时区。");
        }
    }
}
