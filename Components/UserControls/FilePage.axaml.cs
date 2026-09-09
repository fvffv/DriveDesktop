using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.Styling;
using drive_desktop.Models;
using drive_desktop.ViewModels;
using Avalonia.Labs.Gif;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace drive_desktop.Components.UserControls;

public partial class FilePage : UserControl
{
    private readonly IStyle _officialSkeletonLightStyle;
    private readonly IStyle _officialSkeletonDarkStyle;
    private IStyle? _activeOfficialSkeletonStyle;

    public FilePage()
    {
        InitializeComponent();

        _officialSkeletonLightStyle =
            (IStyle)Resources["OfficialSkeletonLightStyle"]!;
        _officialSkeletonDarkStyle =
            (IStyle)Resources["OfficialSkeletonDarkStyle"]!;

        // Ursa 2.2.0 的 Skeleton 原版动画会保留启动时解析出的主题画刷。
        // Light/Dark 分别使用一份由 XAML 预编译的 Ursa 官方样式实例。
        AttachedToVisualTree += (_, _) => ApplyOfficialSkeletonStyle();
        ActualThemeVariantChanged += (_, _) =>
            Dispatcher.UIThread.Post(ApplyOfficialSkeletonStyle, DispatcherPriority.Loaded);
    }

    private void ApplyOfficialSkeletonStyle()
    {
        var targetStyle = ActualThemeVariant == ThemeVariant.Dark
            ? _officialSkeletonDarkStyle
            : _officialSkeletonLightStyle;

        if (ReferenceEquals(_activeOfficialSkeletonStyle, targetStyle)) return;

        if (_activeOfficialSkeletonStyle is not null)
        {
            SkeletonPanel.Styles.Remove(_activeOfficialSkeletonStyle);
        }

        // 只刷新骨架区域的样式，不能修改 FilePage.Styles；否则 Avalonia 会重新应用
        // 面包屑和 FileCard 的 Background，从而跳过它们已有的 BrushTransition。
        SkeletonPanel.Styles.Add(targetStyle);
        _activeOfficialSkeletonStyle = targetStyle;
    }

    /*public class LocalGifSource : IGifSource, IDisposable
    {
        private readonly MemoryStream _stream;
        private readonly PixelSize _size;

        public LocalGifSource(Uri assetUri)
        {
            // 1. 将资源读取为内存流，保证流可以被安全 Seek
            using var assetStream = AssetLoader.Open(assetUri);
            _stream = new MemoryStream();
            assetStream.CopyTo(_stream);
            _stream.Position = 0;

            // 2. 利用 Avalonia 原生 Bitmap 解码第一帧来获取图像的像素尺寸
            using var bitmap = new Bitmap(_stream);
            _size = bitmap.PixelSize;

            // 3. 读完尺寸后，务必把流位置归零，留给 GifImage 内部去播放
            _stream.Position = 0;
        }

        // 实现 IGifSource 的两个成员
        public PixelSize Size => _size;
        public Stream GetStream() => _stream;

        public void Dispose()
        {
            _stream?.Dispose();
        }
    }*/

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
                              FileScrollViewer.Offset.Y + FileScrollViewer.Viewport.Height >=
                              FileScrollViewer.Extent.Height - 50;

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
                Dispatcher.UIThread.Post(() => { popup.IsOpen = false; });
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
