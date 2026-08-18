using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using drive_desktop.Models;
using drive_desktop.ViewModels;

namespace drive_desktop.Components.UserControls;

public partial class FilePage : UserControl
{
    public FilePage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 双击文件夹
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    /// <exception cref="NotImplementedException"></exception>
    private void InputElement_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control uiElement)
        {
            if (uiElement.DataContext is UserDirsInfoItem clickedItem)
            {
                if (this.DataContext is FilePageViewModel vm)
                {
                    vm.FolderClickCommand.Execute(clickedItem);
                }
            }
        }
    }


    private void ScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
  

        CheckLoadNextPage(sender);
    }

    private void Control_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        CheckLoadNextPage(sender);
    }
  
    private void CheckLoadNextPage(object? sender)
    {
        if (sender is ScrollViewer FileScrollViewer)
        {
            // 还没渲染出来时直接跳过
            if (FileScrollViewer.Extent.Height == 0) return;
            
            bool shouldLoad = FileScrollViewer.Viewport.Height >= FileScrollViewer.Extent.Height || 
                              FileScrollViewer.Offset.Y + FileScrollViewer.Viewport.Height >= FileScrollViewer.Extent.Height - 50;

            if (shouldLoad)
            {
                if (this.DataContext is ViewModels.FilePageViewModel vm)
                {
                 
                       vm.LoadNextPageCommand.Execute(null);
                    
                }
            }
        }
      
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

    private void Visual_OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Debug.WriteLine("有一个卡片被创建了！");
    }

    private void InputElement_OnDoubleTapped2(object? sender, TappedEventArgs e)
    {
        if (sender is Control uiElement)
        {
            if (uiElement.DataContext is UserFilesInfoItem clickedItem)
            {
                if (this.DataContext is FilePageViewModel vm)
                {
                    vm.OpenFileCommand.Execute(clickedItem);
                }
            }
        }
    }
}