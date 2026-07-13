using Microsoft.Win32;
using System.Runtime.Versioning;
using GameSaveManager.Application.Startup;

namespace GameSaveManager.Infrastructure.Startup;

/// <summary>使用 HKCU Run 注册当前用户开机启动；无需管理员权限。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GameSaveManager";

    public bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的开机启动注册表项");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return Task.CompletedTask;
        }

        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前客户端的可执行文件路径");
        key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
        return Task.CompletedTask;
    }
}