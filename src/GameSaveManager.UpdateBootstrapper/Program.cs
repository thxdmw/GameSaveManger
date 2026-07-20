using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

return await UpdateBootstrapper.RunAsync(args);

internal static class UpdateBootstrapper
{
    private const int InstallerTimeoutMilliseconds = 10 * 60 * 1000;
    private const int StartupHealthTimeoutMilliseconds = 90 * 1000;

    public static async Task<int> RunAsync(string[] args)
    {
        IReadOnlyDictionary<string, string> options;
        string? transactionPath = null;
        try
        {
            options = ParseOptions(args);
            transactionPath = Required(options, "transaction");
            string installerPath = Required(options, "new-installer");
            string installerSha256 = Required(options, "new-sha256");
            string installerPublisherSha256 = Required(options, "new-publisher-sha256");
            string previousInstallerPath = Required(options, "previous-installer");
            string previousPublisherSha256 = Required(options, "previous-publisher-sha256");
            string applicationPath = Required(options, "app");
            string healthPath = Required(options, "health-file");
            string healthToken = Required(options, "health-token");
            string fromVersion = Required(options, "from-version");
            string toVersion = Required(options, "to-version");
            int waitProcessId = int.Parse(Required(options, "wait-pid"), System.Globalization.CultureInfo.InvariantCulture);

            WriteState(transactionPath, "waiting_for_exit", fromVersion, toVersion, null);
            if (!await WaitForProcessExitAsync(waitProcessId, 60_000))
                throw new InvalidOperationException("客户端未能在 60 秒内完全退出，更新已取消。");

            if (!VerifySignedFile(installerPath, installerSha256, installerPublisherSha256, out string? verificationError))
                throw new CryptographicException($"新版本安装包二次验证失败：{verificationError}");
            WriteState(transactionPath, "installing", fromVersion, toVersion, null);
            if (!RunInstaller(installerPath))
                return RollBack(
                    transactionPath,
                    previousInstallerPath,
                    applicationPath,
                    fromVersion,
                    toVersion,
                    previousPublisherSha256,
                    "新版本安装程序执行失败或超时。");

            try
            {
                TryDelete(healthPath);
                WriteState(transactionPath, "waiting_for_health", fromVersion, toVersion, null);
                using Process application = StartApplication(applicationPath, healthPath, healthToken);
                if (await WaitForHealthAsync(application, healthPath, healthToken))
                {
                    WriteState(transactionPath, "completed", fromVersion, toVersion, null);
                    return 0;
                }
                return RollBack(
                    transactionPath,
                    previousInstallerPath,
                    applicationPath,
                    fromVersion,
                    toVersion,
                    previousPublisherSha256,
                    "新版本未在 90 秒内完成启动确认。");
            }
            catch (Exception exception)
            {
                return RollBack(
                    transactionPath,
                    previousInstallerPath,
                    applicationPath,
                    fromVersion,
                    toVersion,
                    previousPublisherSha256,
                    $"新版本启动确认失败：{exception.Message}");
            }
        }
        catch (Exception exception)
        {
            if (!string.IsNullOrWhiteSpace(transactionPath))
                WriteState(transactionPath, "failed", string.Empty, string.Empty, exception.Message);
            ShowError($"客户端更新未完成：{exception.Message}");
            return 1;
        }
    }

    private static int RollBack(
        string transactionPath,
        string previousInstallerPath,
        string applicationPath,
        string fromVersion,
        string toVersion,
        string previousPublisherSha256,
        string reason)
    {
        WriteState(transactionPath, "rolling_back", fromVersion, toVersion, reason);
        if (!VerifySignedFile(previousInstallerPath, null, previousPublisherSha256, out string? verificationError) ||
            !RunInstaller(previousInstallerPath))
        {
            string failure = string.IsNullOrWhiteSpace(verificationError)
                ? reason
                : $"{reason} 回滚包验证失败：{verificationError}";
            WriteState(transactionPath, "rollback_failed", fromVersion, toVersion, failure);
            ShowError($"{reason}\n\n未能自动恢复上一版本，请手动重新安装 {fromVersion}。用户数据不会被删除。");
            return 2;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = applicationPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(applicationPath) ?? Environment.CurrentDirectory
        });
        WriteState(transactionPath, "rolled_back", fromVersion, toVersion, reason);
        ShowError($"{reason}\n\n已自动恢复到 {fromVersion}，客户端将重新启动。用户数据未被删除。");
        return 3;
    }

    private static bool RunInstaller(string installerPath)
    {
        if (!File.Exists(installerPath)) return false;
        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Environment.CurrentDirectory
        };
        foreach (string argument in new[]
                 {
                     "/VERYSILENT",
                     "/SUPPRESSMSGBOXES",
                     "/CLOSEAPPLICATIONS",
                     "/NORESTART"
                 })
            startInfo.ArgumentList.Add(argument);
        using Process? process = Process.Start(startInfo);
        if (process is null || !process.WaitForExit(InstallerTimeoutMilliseconds))
        {
            try { process?.Kill(true); } catch { }
            return false;
        }
        return process.ExitCode == 0;
    }

    private static bool VerifySignedFile(
        string filePath,
        string? expectedFileSha256,
        string expectedPublisherSha256,
        out string? error)
    {
        error = null;
        if (!File.Exists(filePath))
        {
            error = "文件不存在。";
            return false;
        }
        if (!TryNormalizeSha256(expectedPublisherSha256, out string? normalizedPublisher))
        {
            error = "发布者证书指纹格式无效。";
            return false;
        }
        if (expectedFileSha256 is not null)
        {
            if (!TryNormalizeSha256(expectedFileSha256, out string? normalizedFileHash))
            {
                error = "安装包摘要格式无效。";
                return false;
            }
            using FileStream stream = File.OpenRead(filePath);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!CryptographicEquals(actualHash, normalizedFileHash!))
            {
                error = "安装包 SHA-256 已变化。";
                return false;
            }
        }

        var fileInfo = new WinTrustFileInfo(filePath);
        IntPtr fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var trustData = new WinTrustData(fileInfoPointer);
            Guid action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            int trustResult = WinVerifyTrust(IntPtr.Zero, action, ref trustData);
            if (trustResult != 0)
            {
                error = $"WinVerifyTrust 返回 0x{trustResult:X8}。";
                return false;
            }
#pragma warning disable SYSLIB0057
            using X509Certificate signer = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            string actualPublisher = signer.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();
            if (!CryptographicEquals(actualPublisher, normalizedPublisher!))
            {
                error = "发布者证书与预期不一致。";
                return false;
            }
            return true;
        }
        catch (CryptographicException exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    private static bool TryNormalizeSha256(string? value, out string? normalized)
    {
        normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            normalized = null;
            return false;
        }
        return true;
    }

    private static Process StartApplication(string applicationPath, string healthPath, string healthToken)
    {
        if (!File.Exists(applicationPath)) throw new FileNotFoundException("安装后的客户端不存在。", applicationPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = applicationPath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(applicationPath) ?? Environment.CurrentDirectory
        };
        startInfo.ArgumentList.Add("--update-health-file");
        startInfo.ArgumentList.Add(healthPath);
        startInfo.ArgumentList.Add("--update-health-token");
        startInfo.ArgumentList.Add(healthToken);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动更新后的客户端。");
    }

    private static async Task<bool> WaitForHealthAsync(Process application, string healthPath, string healthToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(StartupHealthTimeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(healthPath))
            {
                string content = await File.ReadAllTextAsync(healthPath);
                if (CryptographicEquals(content.Trim(), healthToken)) return true;
            }
            if (application.HasExited) return false;
            await Task.Delay(500);
        }
        try { if (!application.HasExited) application.Kill(true); } catch { }
        return false;
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId, int timeoutMilliseconds)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            using var cancellation = new CancellationTokenSource(timeoutMilliseconds);
            await process.WaitForExitAsync(cancellation.Token);
            return true;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("更新参数格式无效。");
            options[args[index][2..]] = args[index + 1];
        }
        return options;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"缺少更新参数 --{name}。");

    private static void WriteState(string path, string state, string fromVersion, string toVersion, string? message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string Safe(string? value) => (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        string content = string.Join(Environment.NewLine, new[]
        {
            $"state={Safe(state)}",
            $"from={Safe(fromVersion)}",
            $"to={Safe(toVersion)}",
            $"updated_at_utc={DateTimeOffset.UtcNow:O}",
            $"message={Safe(message)}"
        });
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }

    private static bool CryptographicEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void ShowError(string message) =>
        MessageBox(IntPtr.Zero, message, "GameSave Manager 安全更新", 0x00000010 | 0x00000000);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr windowHandle, string text, string caption, uint type);

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
        public uint UiChoice = 2;
        public uint RevocationChecks = 1;
        public uint UnionChoice = 1;
        public IntPtr FileInfo = fileInfo;
        public uint StateAction = 0;
        public IntPtr StateData = IntPtr.Zero;
        public string? UrlReference = null;
        public uint ProviderFlags = 0x00000080;
        public uint UiContext = 0;
        public IntPtr SignatureSettings = IntPtr.Zero;
    }
}
