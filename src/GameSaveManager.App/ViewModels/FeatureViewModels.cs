using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Security;

namespace GameSaveManager.App.ViewModels;

/// <summary>跨页面共享的轻量会话；设备 Token 始终按需从凭据管理器读取。</summary>
public sealed class ApplicationSession(
    Func<string> serverAddress,
    Func<bool> isAuthenticated,
    Func<string> username,
    Func<CloudGame?> selectedGame,
    Action<CloudGame?> selectGame,
    ICredentialStore credentialStore)
{
    public string ServerAddress => serverAddress();
    public bool IsAuthenticated => isAuthenticated();
    public string Username => username();
    public CloudGame? SelectedGame { get => selectedGame(); set => selectGame(value); }

    public Task<string?> ReadDeviceTokenAsync(Uri server, CancellationToken cancellationToken) =>
        credentialStore.ReadAsync(CredentialTargets.ForDeviceToken(server), cancellationToken);
}

public abstract class FeatureViewModel(MainViewModel owner)
{
    protected MainViewModel Owner { get; } = owner;
}

public sealed class AccountViewModel(MainViewModel owner) : FeatureViewModel(owner);
public sealed class GameLibraryViewModel(MainViewModel owner) : FeatureViewModel(owner);
public sealed class GameDetailViewModel(MainViewModel owner) : FeatureViewModel(owner);
public sealed class SyncViewModel(MainViewModel owner) : FeatureViewModel(owner);
public sealed class RestoreViewModel(MainViewModel owner) : FeatureViewModel(owner);
public sealed class SettingsViewModel(MainViewModel owner) : FeatureViewModel(owner);
public sealed class GameLaunchSettingsViewModel(MainViewModel owner) : FeatureViewModel(owner);

/// <summary>详情页和添加向导共用的存档配置状态，不复制存档校验规则。</summary>
public sealed class SaveConfigurationViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;

    public SaveConfigurationViewModel(MainViewModel owner)
    {
        _owner = owner;
        _owner.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
    }

    public string SaveDirectory { get => _owner.SaveDirectory; set => _owner.SaveDirectory = value; }
    public string AdditionalSaveRootPath { get => _owner.AdditionalSaveRootPath; set => _owner.AdditionalSaveRootPath = value; }
    public string RegistrySaveKeyPath { get => _owner.RegistrySaveKeyPath; set => _owner.RegistrySaveKeyPath = value; }
    public bool IsAutoSyncEnabled => _owner.IsAutoSyncEnabled;
    public string SaveDirectoryPreviewText => _owner.SaveDirectoryPreviewText;
    public string SaveDirectoryConfirmationText => _owner.SaveDirectoryConfirmationText;
    public string AutoSyncConfigurationText => _owner.AutoSyncConfigurationText;
    public ObservableCollection<SaveLocationCandidate> SaveLocationCandidates => _owner.SaveLocationCandidates;
    public SaveLocationCandidate? SelectedSaveLocationCandidate
    {
        get => _owner.SelectedSaveLocationCandidate;
        set => _owner.SelectedSaveLocationCandidate = value;
    }
    public ObservableCollection<SaveRootRule> AdditionalSaveRoots => _owner.AdditionalSaveRoots;
    public SaveRootRule? SelectedAdditionalSaveRoot
    {
        get => _owner.SelectedAdditionalSaveRoot;
        set => _owner.SelectedAdditionalSaveRoot = value;
    }
    public ObservableCollection<RegistrySaveRule> RegistrySaveRules => _owner.RegistrySaveRules;
    public RegistrySaveRule? SelectedRegistrySaveRule
    {
        get => _owner.SelectedRegistrySaveRule;
        set => _owner.SelectedRegistrySaveRule = value;
    }

    public ICommand SuggestSaveDirectoriesCommand => _owner.SuggestSaveDirectoriesCommand;
    public ICommand PreviewSaveDirectoryCommand => _owner.PreviewSaveDirectoryCommand;
    public ICommand ConfirmSaveDirectoryCommand => _owner.ConfirmSaveDirectoryCommand;
    public ICommand AddAdditionalSaveRootCommand => _owner.AddAdditionalSaveRootCommand;
    public ICommand RemoveAdditionalSaveRootCommand => _owner.RemoveAdditionalSaveRootCommand;
    public ICommand AddRegistrySaveRuleCommand => _owner.AddRegistrySaveRuleCommand;
    public ICommand RemoveRegistrySaveRuleCommand => _owner.RemoveRegistrySaveRuleCommand;
    public ICommand StartAutoSnapshotCommand => _owner.StartAutoSnapshotCommand;
    public ICommand StopAutoSnapshotCommand => _owner.StopAutoSnapshotCommand;

    public MainViewModel Host => _owner;
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>六步添加向导的页面状态；云端游戏仅由最后一步提交命令创建。</summary>
public sealed class AddGameWizardViewModel(MainViewModel owner) : FeatureViewModel(owner), INotifyPropertyChanged
{
    private int _step = 1;
    private bool _launchValidated;
    private bool _enableAutomaticBackup;
    private string _workingDirectory = string.Empty;
    private string _arguments = string.Empty;
    private bool _runAsAdministrator;
    private string _monitoredProcessName = string.Empty;
    public int Step
    {
        get => _step;
        set
        {
            int normalized = Math.Clamp(value, 1, 6);
            if (_step == normalized) return;
            _step = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Step)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StepTitle)));
        }
    }

    public string StepTitle => Step switch
    {
        1 => "来源",
        2 => "身份与启动",
        3 => "存档检测",
        4 => "预览确认",
        5 => "保护方式",
        _ => "最终提交"
    };
    public bool LaunchValidated
    {
        get => _launchValidated;
        set
        {
            if (_launchValidated == value) return;
            _launchValidated = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LaunchValidated)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LaunchValidationText)));
        }
    }
    public string LaunchValidationText => LaunchValidated ? "启动验证已通过" : "启动配置待验证";
    public bool EnableAutomaticBackup
    {
        get => _enableAutomaticBackup;
        set
        {
            if (_enableAutomaticBackup == value) return;
            _enableAutomaticBackup = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EnableAutomaticBackup)));
        }
    }
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set { if (_workingDirectory != value) { _workingDirectory = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkingDirectory))); } }
    }
    public string Arguments
    {
        get => _arguments;
        set { if (_arguments != value) { _arguments = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Arguments))); } }
    }
    public bool RunAsAdministrator
    {
        get => _runAsAdministrator;
        set { if (_runAsAdministrator != value) { _runAsAdministrator = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunAsAdministrator))); } }
    }
    public string MonitoredProcessName
    {
        get => _monitoredProcessName;
        set { if (_monitoredProcessName != value) { _monitoredProcessName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MonitoredProcessName))); } }
    }
    public MainViewModel Host => Owner;
    public SaveConfigurationViewModel SaveConfiguration => Owner.SaveConfiguration;
    public event PropertyChangedEventHandler? PropertyChanged;
}
