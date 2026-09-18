using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using drive_desktop.Models;
using Drive.Plugin.Abi;
using Drive.Plugin.SDK;

namespace drive_desktop.Services.Plugins;

public sealed class PluginMenuAction(string title, Action execute)
{
    public string Title { get; } = title;
    public ICommand Command { get; } = new RelayCommand(execute);
}

public sealed class PluginUiRegistry : ObservableObject
{
    private sealed record Registration(PluginInfo Plugin, PluginAction Action);
    private readonly Dictionary<string, Registration> _actions = new(StringComparer.Ordinal);
    public static PluginUiRegistry Shared { get; } = new();
    public ObservableCollection<PluginMenuAction> ToolbarActions { get; } = new();
    public bool HasToolbarActions => ToolbarActions.Count != 0;
    internal Func<FileEntry[]>? SelectionProvider { get; set; }
    internal Action<string, DriveEvent>? EventPublisher { get; set; }
    /// <summary>菜单注册或移除后在宿主 UI 线程通知条目菜单刷新。</summary>
    public event EventHandler? MenusChanged;

    internal void Register(PluginInfo plugin, PluginAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Id) || action.Id.Length > 64 || action.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_') ||
            string.IsNullOrWhiteSpace(action.Title) || action.Title.Length > 64 || !Enum.IsDefined(action.Location) ||
            action.Extensions is null || action.Extensions.Length > 32 || action.Extensions.Any(x => x is null || x.Length > 32))
            throw new PluginException(PluginError.InvalidArgument, "无效插件菜单项。");
        var required = action.Location switch
        {
            PluginActionLocation.FileMenu => PluginPermission.UiFileMenu,
            PluginActionLocation.FolderMenu => PluginPermission.UiFolderMenu,
            _ => PluginPermission.UiMenu
        };
        if ((plugin.Permissions & required) == 0)
            throw new PluginException(PluginError.PermissionDenied, "未申请此菜单位置的权限：" + string.Join(", ", PluginPermissions.Names(required)));
        if (action.Location != PluginActionLocation.Toolbar && (plugin.Permissions & PluginPermission.FileRead) == 0)
            throw new PluginException(PluginError.PermissionDenied, "文件和目录菜单还需要 file.read 权限。");
        var key = Key(plugin.Id, action.Id);
        if (!_actions.ContainsKey(key) && _actions.Values.Count(x => x.Plugin.Id == plugin.Id) >= 32)
            throw new PluginException(PluginError.TooLarge, "每个插件最多注册 32 个操作。");
        _actions[key] = new(plugin, action with { Extensions = action.Extensions.ToArray() });
        RebuildToolbar();
    }
    internal void Unregister(string pluginId, string actionId) { _actions.Remove(Key(pluginId, actionId)); RebuildToolbar(); }
    internal void RemovePlugin(string pluginId)
    {
        foreach (var key in _actions.Where(x => x.Value.Plugin.Id == pluginId).Select(x => x.Key).ToArray()) _actions.Remove(key);
        RebuildToolbar();
    }
    private void RebuildToolbar()
    {
        ToolbarActions.Clear();
        foreach (var registration in _actions.Values.Where(x => x.Action.Location == PluginActionLocation.Toolbar))
            ToolbarActions.Add(new(registration.Plugin.Name + " · " + registration.Action.Title,
                () => Invoke(registration, SelectionProvider?.Invoke() ?? [])));
        OnPropertyChanged(nameof(HasToolbarActions));
        MenusChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Invoke(Registration registration, FileEntry[] selection)
    {
        if (!_actions.TryGetValue(Key(registration.Plugin.Id, registration.Action.Id), out var current) || !ReferenceEquals(current, registration)) return;
        if ((registration.Plugin.Permissions & PluginPermission.FileRead) == 0) selection = [];
        EventPublisher?.Invoke(registration.Plugin.Id, new DriveEvent { Id = DriveEventId.ActionInvoked, ActionId = registration.Action.Id, Selection = selection,
            FileIds = selection.Where(x => !x.IsFolder).Select(x => x.Id).ToArray(),
            FolderIds = selection.Where(x => x.IsFolder).Select(x => x.Id).ToArray(), FolderId = selection.FirstOrDefault()?.FolderId ?? "" });
    }
    /// <summary>为指定条目生成插件菜单快照，只包含已获准且匹配类型和后缀的注册。</summary>
    /// <param name="location">文件或目录菜单位置。</param>
    /// <param name="context">菜单所属文件或目录的数据对象；不能使用其他条目的选中状态替代。</param>
    /// <returns>按注册顺序排列的操作，点击后只通知所属插件。</returns>
    public PluginMenuAction[] GetItemActions(PluginActionLocation location, object? context)
    {
        FileEntry? selected = context switch
        {
            UserFilesInfoItem file => PluginDtoMapper.File(file),
            UserDirsInfoItem folder => PluginDtoMapper.Folder(folder),
            _ => null
        };
        if (selected is null || location != (selected.IsFolder ? PluginActionLocation.FolderMenu : PluginActionLocation.FileMenu)) return [];
        return _actions.Values.Where(x => x.Action.Location == location &&
                (selected.IsFolder || x.Action.Extensions.Length == 0 || x.Action.Extensions.Contains(Path.GetExtension(selected.Name), StringComparer.OrdinalIgnoreCase)))
            .Select(x => new PluginMenuAction(x.Plugin.Name + " · " + x.Action.Title, () => Invoke(x, [selected]))).ToArray();
    }

    /// <summary>保留文件卡片原有的插件右键入口，与三个点菜单共用注册和回调规则。</summary>
    public void AttachFileMenus(Control control)
    {
        control.AddHandler(Control.ContextRequestedEvent, (_, args) =>
        {
            if (args.Handled || args.Source is not Control source) return;
            FileEntry? selected = source.DataContext switch
            {
                UserFilesInfoItem file => PluginDtoMapper.File(file),
                UserDirsInfoItem folder => PluginDtoMapper.Folder(folder),
                _ => null
            };
            if (selected is null) return;
            var location = selected.IsFolder ? PluginActionLocation.FolderMenu : PluginActionLocation.FileMenu;
            var actions = GetItemActions(location, source.DataContext);
            if (actions.Length == 0) return;
            var menu = new ContextMenu();
            foreach (var action in actions)
            {
                var item = new MenuItem { Header = action.Title, Command = action.Command };
                menu.Items.Add(item);
            }
            menu.Open(source);
            args.Handled = true;
        }, RoutingStrategies.Bubble);
    }
    private static string Key(string pluginId, string actionId) => pluginId + ":" + actionId;
}
