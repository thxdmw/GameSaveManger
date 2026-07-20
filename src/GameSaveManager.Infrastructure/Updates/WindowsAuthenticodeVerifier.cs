using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GameSaveManager.Infrastructure.Updates;

public sealed class WindowsAuthenticodeVerifier : IAuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public AuthenticodeVerificationResult Verify(string filePath)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, null, "Authenticode 只能在 Windows 上验证。");
        if (!File.Exists(filePath)) return new(false, null, "待验证文件不存在。");

        var fileInfo = new WinTrustFileInfo(filePath);
        IntPtr fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var trustData = new WinTrustData(fileInfoPointer);
            int result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref trustData);
            if (result != 0)
                return new(false, null, $"WinVerifyTrust 返回 0x{result:X8}。");

#pragma warning disable SYSLIB0057 // .NET 目前没有 X509CertificateLoader 的已签 PE 等价 API。
            using X509Certificate signer = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            string publisherHash = signer.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();
            return new(true, publisherHash, null);
        }
        catch (CryptographicException exception)
        {
            return new(false, null, exception.Message);
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo(string filePath)
    {
        public uint StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        public string FilePath = filePath;
        public IntPtr FileHandle = IntPtr.Zero;
        public IntPtr KnownSubject = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData(IntPtr fileInfo)
    {
        public uint StructSize = (uint)Marshal.SizeOf<WinTrustData>();
        public IntPtr PolicyCallbackData = IntPtr.Zero;
        public IntPtr SipClientData = IntPtr.Zero;
        public uint UiChoice = 2; // WTD_UI_NONE
        public uint RevocationChecks = 1; // WTD_REVOKE_WHOLECHAIN
        public uint UnionChoice = 1; // WTD_CHOICE_FILE
        public IntPtr FileInfo = fileInfo;
        public uint StateAction = 0;
        public IntPtr StateData = IntPtr.Zero;
        public string? UrlReference = null;
        public uint ProviderFlags = 0x00000080; // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
        public uint UiContext = 0;
        public IntPtr SignatureSettings = IntPtr.Zero;
    }
}
