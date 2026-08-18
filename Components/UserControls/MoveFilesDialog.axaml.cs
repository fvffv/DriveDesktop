using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using drive_desktop.Models;
using drive_desktop.ViewModels;

namespace drive_desktop.Components.UserControls;

public partial class MoveFilesDialog : UserControl
{
    public MoveFilesDialog()
    {
        InitializeComponent();
    }

    private void InputElement_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control uiElement)
        {
            if (uiElement.DataContext is UserDirsInfoItem clickedItem)
            {
                if (this.DataContext is MoveFilesViewModel vm)
                {
                    vm.FoloderDoubleCommand.Execute(clickedItem);
                }
            }
        }
    }
}