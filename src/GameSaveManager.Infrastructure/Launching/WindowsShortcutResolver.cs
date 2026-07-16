using System.Runtime.InteropServices;
using GameSaveManager.Application.Launching;

namespace GameSaveManager.Infrastructure.Launching;

/// <summary>通过 Windows Script Host 解析快捷方式，COM 对象仅存在于基础设施层。</summary>
public sealed class WindowsShortcutResolver : IShortcutResolver
{
    public Task<ShortcutResolution> ResolveAsync(string shortcutPath, CancellationToken cancellationToken) =>
        Task.Run(() => Resolve(shortcutPath, cancellationToken), cancellationToken);

    private static ShortcutResolution Resolve(string shortcutPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(shortcutPath);
        if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".lnk", StringComparison.OrdinalIgnoreCase))
            return new ShortcutResolution(false, fullPath, null, null, null, null, "快捷方式文件不存在。");

        object? shell = null;
        object? shortcut = null;
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) throw new InvalidOperationException("当前系统不支持 Windows Script Host。");
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [fullPath]);
            Type shortcutType = shortcut.GetType();
            string? target = shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)?.ToString();
            string? arguments = shortcutType.InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)?.ToString();
            string? workingDirectory = shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)?.ToString();
            string? iconLocation = shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)?.ToString();
            if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
                return new ShortcutResolution(false, fullPath, target, arguments, workingDirectory, iconLocation, "快捷方式目标不存在或无法访问。");
            return new ShortcutResolution(true, fullPath, Path.GetFullPath(target), arguments,
                string.IsNullOrWhiteSpace(workingDirectory) ? Path.GetDirectoryName(target) : Path.GetFullPath(workingDirectory),
                iconLocation, null);
        }
        catch (Exception exception)
        {
            return new ShortcutResolution(false, fullPath, null, null, null, null, exception.Message);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}
