using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GameSaveManager.App.Common;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SaveManifestBuilder _manifestBuilder;
    private string _saveDirectory = string.Empty;
    private string _statusText = "输入游戏存档目录，构建第一份内容清单。";
    private int _fileCount;
    private string _logicalSizeText = "0 B";

    public MainViewModel(SaveManifestBuilder manifestBuilder)
    {
        _manifestBuilder = manifestBuilder;
        BuildManifestCommand = new AsyncCommand(BuildManifestAsync);
    }

    public string SaveDirectory
    {
        get => _saveDirectory;
        set => SetField(ref _saveDirectory, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public int FileCount
    {
        get => _fileCount;
        private set => SetField(ref _fileCount, value);
    }

    public string LogicalSizeText
    {
        get => _logicalSizeText;
        private set => SetField(ref _logicalSizeText, value);
    }

    public ICommand BuildManifestCommand { get; }

    private async Task BuildManifestAsync()
    {
        try
        {
            StatusText = "正在扫描目录并计算变化文件的 SHA-256…";
            IReadOnlyList<SnapshotFile> files = await _manifestBuilder.BuildAsync(
                SaveDirectory,
                CancellationToken.None);
            FileCount = files.Count;
            LogicalSizeText = FormatBytes(files.Sum(file => file.Size));
            StatusText = "Manifest 已构建。下一阶段将接入 SQLite 与 GameSave Sync API。";
        }
        catch (Exception exception)
        {
            StatusText = $"扫描失败：{exception.Message}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
