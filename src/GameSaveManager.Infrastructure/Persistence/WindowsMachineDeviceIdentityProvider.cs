using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using GameSaveManager.Application.Device;
using Microsoft.Win32;

namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>从 Windows 安装级 MachineGuid 派生应用专用标识；不上传原始 MachineGuid。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMachineDeviceIdentityProvider : IDeviceIdentityProvider
{
    public Task<string> GetOrCreateDeviceIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: false);
        string? machineGuid = key?.GetValue("MachineGuid") as string;
        if (string.IsNullOrWhiteSpace(machineGuid))
            throw new InvalidOperationException("无法读取 Windows 安装级设备标识。");
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes("GameSaveManager/device/v1/" + machineGuid.Trim()));
        return Task.FromResult("win-" + Convert.ToHexString(digest).ToLowerInvariant()[..40]);
    }
}
