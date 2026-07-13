using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameSaveManager.Application.Diagnostics;

namespace GameSaveManager.Infrastructure.Diagnostics;

/// <summary>按天写入 JSON Lines 的轻量日志器，并在落盘前脱敏常见凭据格式。</summary>
public sealed partial class JsonFileLogger : IAppLogger
{
    private const int RetentionDays = 14;
    private const int MaximumTextLength = 4096;
    private readonly object _syncRoot = new();
    private readonly string _logDirectory;

    public JsonFileLogger(string? logDirectory = null)
    {
        string preferredDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameSaveManager",
            "logs");
        try
        {
            Directory.CreateDirectory(preferredDirectory);
            _logDirectory = preferredDirectory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logDirectory = Path.Combine(Path.GetTempPath(), "GameSaveManager", "logs");
            Directory.CreateDirectory(_logDirectory);
        }
        CleanupExpiredLogs();
    }

    public void Information(string eventName, string message) =>
        Write("Information", eventName, message, null);

    public void Error(string eventName, Exception exception, string message) =>
        Write("Error", eventName, message, exception);

    private void Write(string level, string eventName, string message, Exception? exception)
    {
        try
        {
            var entry = new LogEntry(
                DateTimeOffset.UtcNow,
                level,
                Sanitize(eventName),
                Sanitize(message),
                exception?.GetType().FullName,
                exception is null ? null : Sanitize(exception.Message),
                Environment.ProcessId);
            string line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            string path = Path.Combine(_logDirectory, $"gamesave-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            lock (_syncRoot)
            {
                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // 日志失败不能中断存档扫描、同步或恢复等核心业务。
        }
    }

    private void CleanupExpiredLogs()
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        foreach (string path in Directory.EnumerateFiles(_logDirectory, "gamesave-*.jsonl"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
            }
            catch (IOException)
            {
                // 被其他进程占用的旧日志留到下次启动再清理。
            }
            catch (UnauthorizedAccessException)
            {
                // 权限异常只影响日志保留，不影响主程序启动。
            }
        }
    }

    private static string Sanitize(string value)
    {
        string sanitized = BearerPattern().Replace(value ?? string.Empty, "Bearer ***");
        sanitized = SecretFieldPattern().Replace(sanitized, "$1=***");
        return sanitized.Length <= MaximumTextLength
            ? sanitized
            : sanitized[..MaximumTextLength] + "…";
    }

    [GeneratedRegex("(?i)Bearer\\s+[A-Za-z0-9._~+\\-/]+=*")]
    private static partial Regex BearerPattern();

    [GeneratedRegex("(?i)\\b(password|deviceToken|accessToken|secret|apiKey|token)\\b\\s*[:=]\\s*[^\\s,;]+")]
    private static partial Regex SecretFieldPattern();

    private sealed record LogEntry(
        DateTimeOffset TimestampUtc,
        string Level,
        string EventName,
        string Message,
        string? ExceptionType,
        string? ExceptionMessage,
        int ProcessId);
}