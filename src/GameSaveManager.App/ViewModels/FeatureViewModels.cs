using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows.Input;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Files;
using GameSaveManager.Application.Launching;
using GameSaveManager.Application.Security;
using GameSaveManager.App.Common;

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

/// <summary>详情页和添加向导共用的存档配置状态，不复制存档校验规则。</summary>
public sealed class SaveConfigurationViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;
    private string _includePatternsText = string.Empty;
    private string _excludePatternsText = string.Empty;

    public SaveConfigurationViewModel(MainViewModel owner)
    {
        _owner = owner;
        _owner.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
        ApplySelectedRootPatternsCommand = new DelegateCommand(_ => ApplySelectedRootPatterns(), _ => SelectedAdditionalSaveRoot is not null);
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
    public ObservableCollection<SaveRootPreview> SaveRootPreviews => _owner.SaveRootPreviews;
    public ObservableCollection<RegistrySavePreview> RegistrySavePreviews => _owner.RegistrySavePreviews;
    public SaveRootRule? SelectedAdditionalSaveRoot
    {
        get => _owner.SelectedAdditionalSaveRoot;
        set
        {
            _owner.SelectedAdditionalSaveRoot = value;
            IncludePatternsText = value is null ? string.Empty : string.Join(Environment.NewLine, value.IncludePatterns);
            ExcludePatternsText = value is null ? string.Empty : string.Join(Environment.NewLine, value.ExcludePatterns);
            if (ApplySelectedRootPatternsCommand is DelegateCommand command) command.RaiseCanExecuteChanged();
        }
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
    public ICommand ApplySelectedRootPatternsCommand { get; }

    public string IncludePatternsText
    {
        get => _includePatternsText;
        set { if (_includePatternsText != value) { _includePatternsText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncludePatternsText))); } }
    }
    public string ExcludePatternsText
    {
        get => _excludePatternsText;
        set { if (_excludePatternsText != value) { _excludePatternsText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExcludePatternsText))); } }
    }

    private void ApplySelectedRootPatterns()
    {
        if (SelectedAdditionalSaveRoot is null) throw new InvalidOperationException("请先选择附加目录。");
        string[] includes = ParsePatterns(IncludePatternsText);
        string[] excludes = ParsePatterns(ExcludePatternsText);
        _owner.UpdateAdditionalSaveRootRules(SelectedAdditionalSaveRoot with
        {
            IncludePatterns = includes,
            ExcludePatterns = excludes,
            UserConfirmed = false
        });
    }

    private static string[] ParsePatterns(string text) => text
        .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => value.Replace('\\', '/'))
        .Select(value => Path.IsPathRooted(value) || value.Split('/').Contains("..", StringComparer.Ordinal)
            ? throw new InvalidOperationException($"包含/排除规则不能是绝对路径或包含 ..：{value}")
            : value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public MainViewModel Host => _owner;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class DetectedProcessOption : INotifyPropertyChanged
{
    private bool _isSelected;
    private readonly Action _changed;

    public DetectedProcessOption(string processName, bool isSelected, Action changed)
    {
        ProcessName = processName;
        _isSelected = isSelected;
        _changed = changed;
    }

    public string ProcessName { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            _changed();
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>六步添加向导的真实状态机；每一步都由业务条件门禁，最后一步才允许创建云端游戏。</summary>
public sealed class AddGameWizardViewModel : FeatureViewModel, INotifyPropertyChanged
{
    private int _step = 1;
    private bool _launchValidated;
    private bool _enableAutomaticBackup;
    private string _workingDirectory = string.Empty;
    private string _arguments = string.Empty;
    private bool _runAsAdministrator;
    private string _monitoredProcessName = string.Empty;
    private bool _updatingDetectedProcesses;

    public AddGameWizardViewModel(MainViewModel owner) : base(owner)
    {
        owner.PropertyChanged += Owner_OnPropertyChanged;
        owner.SaveLocationCandidates.CollectionChanged += Collection_OnCollectionChanged;
        owner.AdditionalSaveRoots.CollectionChanged += Collection_OnCollectionChanged;
        owner.RegistrySaveRules.CollectionChanged += Collection_OnCollectionChanged;
    }

    public ObservableCollection<DetectedProcessOption> DetectedProcessOptions { get; } = [];
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
            RefreshValidation();
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
            RefreshValidation();
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
            RefreshValidation();
        }
    }
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set { if (_workingDirectory != value) { _workingDirectory = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkingDirectory))); InvalidateLaunchValidation(); } }
    }
    public string Arguments
    {
        get => _arguments;
        set { if (_arguments != value) { _arguments = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Arguments))); InvalidateLaunchValidation(); } }
    }
    public bool RunAsAdministrator
    {
        get => _runAsAdministrator;
        set { if (_runAsAdministrator != value) { _runAsAdministrator = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunAsAdministrator))); InvalidateLaunchValidation(); } }
    }
    public string MonitoredProcessName
    {
        get => _monitoredProcessName;
        set { if (_monitoredProcessName != value) { _monitoredProcessName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MonitoredProcessName))); InvalidateLaunchValidation(); } }
    }

    public bool CanMoveNext => string.IsNullOrEmpty(StepValidationMessage);
    public string StepValidationMessage => ValidateStep(Step);
    public bool IsFinalConfigurationValid => string.IsNullOrEmpty(ValidateStep(6));

    public bool TryMoveNext()
    {
        RefreshValidation();
        if (!CanMoveNext || Step >= 6) return false;
        Step++;
        return true;
    }

    public IReadOnlyList<string> GetConfirmedMonitoredProcessNames()
    {
        string[] selected = DetectedProcessOptions.Where(option => option.IsSelected)
            .Select(option => GameProcessNameRules.Normalize(option.ProcessName))
            .Where(name => name.Length > 0 && !GameProcessNameRules.IsUnsafeGenericName(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length > 0) return selected;
        string manual = GameProcessNameRules.Normalize(MonitoredProcessName);
        return manual.Length == 0 || GameProcessNameRules.IsUnsafeGenericName(manual) ? [] : [manual];
    }

    public void SetDetectedProcesses(IEnumerable<string> processNames)
    {
        _updatingDetectedProcesses = true;
        try
        {
            DetectedProcessOptions.Clear();
            foreach (string name in processNames.Select(GameProcessNameRules.Normalize)
                         .Where(name => name.Length > 0 && !GameProcessNameRules.IsUnsafeGenericName(name))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
                DetectedProcessOptions.Add(new DetectedProcessOption(name, true, RefreshValidation));
            string? first = DetectedProcessOptions.FirstOrDefault()?.ProcessName;
            if (first is not null)
            {
                _monitoredProcessName = first;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MonitoredProcessName)));
            }
        }
        finally { _updatingDetectedProcesses = false; }
        RefreshValidation();
    }

    public void InvalidateLaunchValidation()
    {
        if (_updatingDetectedProcesses) return;
        DetectedProcessOptions.Clear();
        LaunchValidated = false;
        RefreshValidation();
    }

    public void RefreshValidation()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanMoveNext)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StepValidationMessage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFinalConfigurationValid)));
        Owner.RefreshWizardCommandState();
    }

    private string ValidateStep(int step)
    {
        if (step == 1 && Owner.SelectedDiscoveredGame is null) return "请选择平台游戏、本地 EXE 或快捷方式。";
        if (step >= 2)
        {
            if (string.IsNullOrWhiteSpace(Owner.NewGameName)) return "请输入游戏名称。";
            if (!Owner.PendingLaunchTargetIsValid) return "请选择存在且有效的启动入口。";
        }
        if (step >= 3 && (string.IsNullOrWhiteSpace(Owner.SaveDirectory) || !Directory.Exists(Owner.SaveDirectory)))
            return "请选择或检测到一个存在的存档目录。";
        if (step >= 4 && (!Owner.IsSaveConfigurationPreviewValid || !Owner.IsSaveDirectoryConfirmed))
            return "请预览完整存档配置并确认所有目录和规则。";
        if (step >= 5 && EnableAutomaticBackup
            && (!LaunchValidated || GetConfirmedMonitoredProcessNames().Count == 0))
            return "启用自动备份前必须重新测试启动并确认监控进程。";
        return string.Empty;
    }

    private void Owner_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.NewGameName)
            or nameof(MainViewModel.AutoSnapshotExecutablePath)
            or nameof(MainViewModel.SaveDirectory)
            or nameof(MainViewModel.IsSaveDirectoryConfirmed)) RefreshValidation();
    }

    private void Collection_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshValidation();
    public void Reset()
    {
        Step = 1;
        LaunchValidated = false;
        EnableAutomaticBackup = false;
        WorkingDirectory = string.Empty;
        Arguments = string.Empty;
        RunAsAdministrator = false;
        MonitoredProcessName = string.Empty;
        DetectedProcessOptions.Clear();
        RefreshValidation();
    }
    public MainViewModel Host => Owner;
    public SaveConfigurationViewModel SaveConfiguration => Owner.SaveConfiguration;
    public event PropertyChangedEventHandler? PropertyChanged;
}
