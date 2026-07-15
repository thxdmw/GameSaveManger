using System.Diagnostics;
using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Infrastructure.Discovery;

public sealed class WindowsExecutableGameIdentityFactory : IExecutableGameIdentityFactory
{
    private static readonly string[] RejectedPrefixes = ["unins", "uninstall", "setup", "crash", "reporter", "redist", "vc_redist"];

    public Task<GameIdentity> CreateAsync(string executablePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            throw new FileNotFoundException("未找到所选的游戏 EXE。", executablePath);

        string fileName = Path.GetFileNameWithoutExtension(executablePath);
        if (RejectedPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("所选 EXE 看起来是安装、卸载或崩溃报告程序，请选择游戏主程序。");

        FileVersionInfo version = FileVersionInfo.GetVersionInfo(executablePath);
        string installDirectory = Path.GetDirectoryName(executablePath)!;
        string name = FirstUsable(version.ProductName, version.FileDescription, fileName, Path.GetFileName(installDirectory));
        return Task.FromResult(new GameIdentity(name, GameIdentity.Local, null, installDirectory, executablePath, Path.GetFileName(executablePath)));
    }

    private static string FirstUsable(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))!.Trim();
}
