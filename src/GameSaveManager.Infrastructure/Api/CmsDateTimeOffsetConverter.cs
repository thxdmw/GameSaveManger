using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameSaveManager.Infrastructure.Api;

/// <summary>
/// 兼容 CMS 返回的日期格式。
/// CMS 的旧日期字段可能没有时区，也可能以 Unix 毫秒时间戳返回；
/// 客户端统一转换为 DateTimeOffset，避免成功响应在反序列化阶段被误判为失败。
/// </summary>
public sealed class CmsDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    private static readonly string[] LocalDateFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"
    ];

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long milliseconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime();
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("CMS 日期字段必须是字符串或 Unix 毫秒时间戳。");
        }

        string? value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("CMS 日期字段不能为空。");
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out DateTimeOffset offsetDateTime))
        {
            return offsetDateTime.ToLocalTime();
        }

        if (DateTime.TryParseExact(
                value,
                LocalDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime localDateTime))
        {
            DateTime unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
        }

        throw new JsonException($"无法识别 CMS 日期格式: {value}");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
}
