using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Drive.Plugin.SDK;

namespace drive_desktop.Services.Plugins;

/// <summary>嵌入文件卡片已有菜单的插件操作列表；打开、注册、停用时刷新。</summary>
public sealed class PluginMenuItems : ItemsControl
{
    /// <summary>要展示的文件或目录菜单位置。</summary>
    public static readonly StyledProperty<PluginActionLocation> LocationProperty =
        AvaloniaProperty.Register<PluginMenuItems, PluginActionLocation>(nameof(Location));
    private bool _attached;

    /// <summary>文件使用 FileMenu，文件夹使用 FolderMenu。</summary>
    public PluginActionLocation Location
    {
        get { return GetValue(LocationProperty); }
        set { SetValue(LocationProperty, value); }
    }

    /// <summary>沿用 ItemsControl 的默认容器样式。</summary>
    protected override Type StyleKeyOverride
    {
        get { return typeof(ItemsControl); }
    }

    /// <summary>用已有菜单按钮样式呈现插件标题和点击命令。</summary>
    public PluginMenuItems()
    {
        ItemTemplate = new FuncDataTemplate<PluginMenuAction>((action, _) =>
        {
            var button = new Button
            {
                Content = new TextBlock { Text = action?.Title, TextTrimming = TextTrimming.CharacterEllipsis },
                Command = action?.Command, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Classes.Add("el-dropdown-item");
            ToolTip.SetTip(button, action?.Title);
            return button;
        });
    }
 
    /// <summary>进入菜单可视树后订阅注册变化，并获取当前条目的快照。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        PluginUiRegistry.Shared.MenusChanged += OnMenusChanged;
        Refresh();
    }

    /// <summary>关闭菜单或回收卡片时解除静态注册表订阅，避免保留旧条目。</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        PluginUiRegistry.Shared.MenusChanged -= OnMenusChanged;
        ItemsSource = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>卡片复用或菜单位置改变时更新对应条目的操作。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_attached && (change.Property == DataContextProperty || change.Property == LocationProperty)) Refresh();
    }

    /// <summary>注册、移除或停用插件后刷新当前已经打开的菜单。</summary>
    private void OnMenusChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    /// <summary>将当前条目专属的菜单快照绑定到 ItemsControl。</summary>
    private void Refresh()
    {
        ItemsSource = PluginUiRegistry.Shared.GetItemActions(Location, DataContext);
    }
}
