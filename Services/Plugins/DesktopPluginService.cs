using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.ViewModels;
using Drive.Plugin.Abi;
using Drive.Plugin.Hosting;
using Drive.Plugin.SDK;

namespace drive_desktop.Services.Plugins;

public sealed partial class DesktopPluginService : ObservableObject
{
    private readonly PluginHostAdapter _adapter;
    private readonly UserInfoService _user;
    private readonly Func<FilePageViewModel> _files;
    private readonly PluginManager _manager;
    private Task? _startup;
    private Guid _lastUserId;
    private bool _stopping;
    private readonly DispatcherTimer _logTimer;
    private long _logVersion = -1;
    private PluginLogEntry[] _logEntries = [];
    public ObservableCollection<PluginItemViewModel> Items { get; } = new();
    public ObservableCollection<PluginItemViewModel> Plugins { get; } = new();
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _logSearchText = "";
    [ObservableProperty] private IReadOnlyList<PluginLogEntry> _logs = Array.Empty<PluginLogEntry>();
    public int PluginCount => Items.Count;
    public int LogCount => _logEntries.Length;
    public bool HasNoPlugins => Plugins.Count == 0;
    public bool HasNoLogs => Logs.Count == 0;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _message = "插件仅应来自你信任的作者。启用即授权下方列出的权限。";
    public bool IsEmpty => Items.Count == 0;
    public string PluginDirectory => _manager.PluginDirectory;
    public string LogPath => _manager.Log.PathName;

    public DesktopPluginService(PluginHostAdapter adapter, UserInfoService user, Func<FilePageViewModel> files)
    {
        _adapter = adapter; _user = user; _files = files; _lastUserId = user.ShowUserInfo.UserId;
        _manager = new(new PluginHostOptions(Path.Combine(AppContext.BaseDirectory, "Plugins"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DriveDesktop", "Plugins")), adapter);
        _manager.Changed += (_, _) => Dispatcher.UIThread.Post(Synchronize);
        _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _logTimer.Tick += (_, _) => RefreshLogs();
        _logTimer.Start();
        PluginEventHub.Published += Publish;
        user.PropertyChanged += UserChanged;
        WeakReferenceMessenger.Default.Register<ThemeService.ThemeChangedMessage>(this, (_, message) =>
            Publish(new() { Id = DriveEventId.ThemeChanged, Theme = message.NewTheme == Avalonia.Styling.ThemeVariant.Dark ? "dark" : "light" }));
        PluginUiRegistry.Shared.SelectionProvider = () =>
        {
            var vm = _files();
            return (vm.FileInfos ?? []).Where(x => x.IsChecked).Select(PluginDtoMapper.File)
                .Concat((vm.FolderInfos ?? []).Where(x => x.IsChecked).Select(x => PluginDtoMapper.Folder(x))).Take(200).ToArray();
        };
        PluginUiRegistry.Shared.EventPublisher = _manager.PublishTo;
    }

    public Task StartAsync() => _startup ??= ScanAsync();
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (_stopping || IsBusy) return;
        IsBusy = true;
        try { await _manager.ScanAsync(); Message = "插件扫描完成。更换或删除已加载的 DLL 后，请重启客户端。"; }
        catch (Exception e) { Message = e.Message; }
        finally { IsBusy = false; Synchronize(); }
    }
    [RelayCommand]
    private void OpenDirectory()
    {
        try { Process.Start(new ProcessStartInfo(PluginDirectory) { UseShellExecute = true }); }
        catch (Exception e) { Message = e.Message; }
    }
    [RelayCommand]
    private void OpenLog()
    {
        try { if (File.Exists(LogPath)) Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true }); else Message = "暂时没有插件日志。"; }
        catch (Exception e) { Message = e.Message; }
    }
    internal async Task EnableAsync(PluginDescriptor descriptor)
    {
        try { await _manager.ApproveAndEnableAsync(descriptor); Message = $"{descriptor.DisplayName} 已启用。"; }
        catch (Exception e) { Message = e.Message; }
        Synchronize();
    }
    internal async Task DisableAsync(PluginDescriptor descriptor)
    {
        try { await _manager.DisableAsync(descriptor); Message = $"{descriptor.DisplayName} 已禁用。"; }
        catch (Exception e) { Message = e.Message; }
        Synchronize();
    }
    internal async Task OpenAsync(PluginDescriptor descriptor)
    {
        try { await _manager.ShowUiAsync(descriptor); }
        catch (Exception e) { Message = e.Message; }
        Synchronize();
    }
    private void Synchronize()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Synchronize); return; }
        foreach (var descriptor in _manager.Plugins)
        {
            var item = Items.FirstOrDefault(x => ReferenceEquals(x.Descriptor, descriptor));
            if (item is null) Items.Add(item = new(this, descriptor));
            item.Refresh();
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(PluginCount));
        FilterPlugins();
    }
    partial void OnSearchTextChanged(string value) => FilterPlugins();
    partial void OnLogSearchTextChanged(string value) => FilterLogs();
    private void FilterPlugins()
    {
        var query = SearchText.Trim();
        var matching = Items.Where(x => query.Length == 0 ||
            $"{x.Name} {x.Author} {x.Description} {x.Version} {string.Join(' ', x.Tags)}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!Plugins.SequenceEqual(matching))
        {
            Plugins.Clear();
            foreach (var item in matching) Plugins.Add(item);
        }
        OnPropertyChanged(nameof(HasNoPlugins));
    }
    private void RefreshLogs()
    {
        var version = _manager.Log.Version;
        if (_logVersion == version) return;
        _logVersion = version;
        _logEntries = _manager.Log.Snapshot();
        OnPropertyChanged(nameof(LogCount));
        FilterLogs();
    }
    private void FilterLogs()
    {
        var query = LogSearchText.Trim();
        Logs = _logEntries.Reverse().Where(x => query.Length == 0 ||
            $"{x.TimeText} {x.LevelText} {x.Level} {x.PluginName} {x.Message}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        OnPropertyChanged(nameof(HasNoLogs));
    }
    private void Publish(DriveEvent data) { if (!_stopping) _manager.Publish(data); }
    private void UserChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserInfoService.ShowUserInfo))
        {
            if (_lastUserId != _user.ShowUserInfo.UserId)
            {
                _lastUserId = _user.ShowUserInfo.UserId;
                _adapter.OnAccountChanged();
                _manager.CancelUserRequests();
            }
            Publish(new() { Id = DriveEventId.UserChanged, User = _adapter.CurrentUser() });
        }
        else if (e.PropertyName == nameof(UserInfoService.CurrentDirectoryId))
            Publish(new() { Id = DriveEventId.FolderChanged, FolderId = _user.CurrentDirectoryId ?? "" });
    }
    public async Task ShutdownAsync()
    {
        _stopping = true;
        _logTimer.Stop();
        PluginEventHub.Published -= Publish; _user.PropertyChanged -= UserChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        await _manager.ShutdownAsync();
    }


    /// <summary>
    /// 换页索引
    /// </summary>
    [ObservableProperty] private int _pageIndex = 0;
    [RelayCommand]
    private void SwitchPage(string page)
    {
        if (int.TryParse(page, out var index) && index is 0 or 1) PageIndex = index;
    }
}

public sealed partial class PluginItemViewModel : ObservableObject
{
    private readonly DesktopPluginService _owner;
    internal PluginDescriptor Descriptor { get; }
    private bool _iconLoaded;
    [ObservableProperty] private bool _isChanging;
    private bool? _requestedEnabled;
    public Bitmap? Icon { get; private set; }
    public bool HasIcon => Icon is not null;
    public string Name => Descriptor.DisplayName;
    public string Author => Descriptor.Info?.Author ?? "";
    public string Version => "v" + (Descriptor.Info?.Version ?? "—");
    public string[] Tags => Descriptor.Info?.Tags ?? [];
    public IBrush IconBackground { get; private set; } = Brushes.DodgerBlue;
    public string ToggleHint => HasError ? Error : $"启用即授权此插件：{Permissions}。插件在独立进程中运行。";
    public string Subtitle => Descriptor.Info is { } info ? $"{info.Author} · v{info.Version}" : "等待元信息";
    public string Description => Descriptor.Info?.Description ?? "";
    public string Permissions => string.Join("、", PluginPermissions.Names(Descriptor.Info?.Permissions ?? PluginPermission.None));
    public string Error => Descriptor.Error;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public string Status => Descriptor.State switch
    {
        PluginState.Discovered => "正在读取", PluginState.AwaitingApproval => "等待授权", PluginState.Initializing => "正在启用",
        PluginState.Enabled => "运行中", PluginState.Disabled => "已停用", PluginState.Incompatible => "版本不兼容", _ => "已隔离"
    };
    public bool CanEnable => Descriptor.State is PluginState.AwaitingApproval or PluginState.Disabled;
    public bool CanDisable => Descriptor.State is PluginState.Enabled or PluginState.Faulted;
    public bool CanOpen => !IsChanging && Descriptor.State == PluginState.Enabled && (Descriptor.Info!.Capabilities & PluginCapabilities.HasUi) != 0;
    public bool CanToggle => !IsChanging && (CanEnable || Descriptor.State == PluginState.Enabled);
    public bool IsRunning => Descriptor.State == PluginState.Enabled;
    public bool IsEnabled
    {
        get => _requestedEnabled ?? IsRunning;
        set
        {
            if (IsChanging || value == IsRunning) return;
            _ = ChangeEnabledAsync(value);
        }
    }
    private async Task ChangeEnabledAsync(bool enabled)
    {
        if (!CanToggle) { OnPropertyChanged(nameof(IsEnabled)); return; }
        _requestedEnabled = enabled; IsChanging = true; Refresh();
        try
        {
            if (enabled) await _owner.EnableAsync(Descriptor);
            else await _owner.DisableAsync(Descriptor);
        }
        finally { _requestedEnabled = null; IsChanging = false; Refresh(); }
    }
    public PluginItemViewModel(DesktopPluginService owner, PluginDescriptor descriptor) { _owner = owner; Descriptor = descriptor; }
    [RelayCommand(CanExecute = nameof(CanEnable))] private Task Enable() => _owner.EnableAsync(Descriptor);
    [RelayCommand(CanExecute = nameof(CanDisable))] private Task Disable() => _owner.DisableAsync(Descriptor);
    [RelayCommand(CanExecute = nameof(CanOpen))] private Task Open() => _owner.OpenAsync(Descriptor);
    internal void Refresh()
    {
        if (!_iconLoaded && Descriptor.Info is { } info)
        {
            _iconLoaded = true;
            try
            {
                var bytes = info.IconIco.Length > 0 ? info.IconIco : info.IconPng;
                if (bytes.Length > 0) { using var stream = new MemoryStream(bytes); Icon = new Bitmap(stream); }
                if (Color.TryParse(info.BackgroundColor, out var color)) IconBackground = new SolidColorBrush(color);
            }
            catch { /* A malformed icon must not prevent disabling or inspecting the plugin. */ }
        }
        OnPropertyChanged(string.Empty);
        EnableCommand.NotifyCanExecuteChanged(); DisableCommand.NotifyCanExecuteChanged(); OpenCommand.NotifyCanExecuteChanged();
    }
}
