
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Avalonia.Threading;
using drive_desktop.Models;
using drive_desktop.ViewModels;

namespace drive_desktop.Components.UserControls;

public partial class SearchPage : UserControl
{
    public SearchPage()
    {
        InitializeComponent();
        Services.Plugins.PluginUiRegistry.Shared.AttachFileMenus(this);
    }
    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Control clickedControl)
        {
            var popup = clickedControl.FindLogicalAncestorOfType<Popup>();
            
            if (popup != null)
            {
               
                Dispatcher.UIThread.Post(() =>
                {
                    popup.IsOpen = false;
                });
            }
        }
    }
    private void InputElement_OnDoubleTapped2(object? sender, TappedEventArgs e)
    {
        if (sender is Control uiElement)
        {
            if (uiElement.DataContext is UserFilesInfoItem clickedItem)
            {
                if (this.DataContext is SearchPageViewModel vm)
                {
                    Services.Plugins.PluginEventHub.Publish(new Drive.Plugin.SDK.DriveEvent
                    {
                        Id = Drive.Plugin.Abi.DriveEventId.FileDoubleClicked,
                        FileIds = [clickedItem.Id], FolderId = clickedItem.FolderId ?? "",
                        Selection = [Services.Plugins.PluginDtoMapper.File(clickedItem)]
                    });
                    vm.OpenFileCommand.Execute(clickedItem);
                }
            }
        }
    }

    private void InputElement_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control uiElement)
        {
            if (uiElement.DataContext is UserDirsInfoItem clickedItem)
            {
                if (this.DataContext is SearchPageViewModel vm)
                {
                    vm.OpenFileOrFolderPFolderCommand.Execute(clickedItem);
                }
            }
        }
    }
}
