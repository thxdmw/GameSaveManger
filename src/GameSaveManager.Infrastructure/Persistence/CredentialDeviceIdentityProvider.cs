using GameSaveManager.Application.Device;
using GameSaveManager.Application.Security;

namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>
/// 把设备标识保存在 Windows Credential Manager，并从旧 SQLite 设置迁移。
/// 因此清理 LocalAppData 后，同一 Windows 用户仍会被识别为原设备。
/// </summary>
public sealed class CredentialDeviceIdentityProvider(
    ICredentialStore credentialStore,
    IDeviceIdentityProvider legacyProvider) : IDeviceIdentityProvider
{
    public async Task<string> GetOrCreateDeviceIdAsync(CancellationToken cancellationToken)
    {
        string? persisted = await credentialStore.ReadAsync(
            CredentialTargets.StableDeviceId, cancellationToken);
        if (IsValid(persisted)) return persisted!;

        string deviceId = await legacyProvider.GetOrCreateDeviceIdAsync(cancellationToken);
        if (!IsValid(deviceId)) throw new InvalidOperationException("生成的设备标识不符合协议要求。");
        await credentialStore.SaveAsync(CredentialTargets.StableDeviceId, deviceId, cancellationToken);
        return deviceId;
    }

    private static bool IsValid(string? value) =>
        value is { Length: >= 8 and <= 64 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
