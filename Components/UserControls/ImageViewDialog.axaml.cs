using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using drive_desktop.ViewModels;

namespace drive_desktop.Components.UserControls;

public partial class ImageViewDialog : UserControl
{
    public ImageViewDialog()
    {
        InitializeComponent();
    }

    private void InputElement_OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control uiElement)
        {
                if (this.DataContext is ImageViewModel vm)
                {
                    vm.CloseCommand.Execute(null);
                }
            
        }
    }
}