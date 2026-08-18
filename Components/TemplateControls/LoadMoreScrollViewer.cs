using System;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace drive_desktop.Components.TemplateControls;

public class LoadMoreScrollViewer : ScrollViewer
{
    protected override Type StyleKeyOverride =>
        typeof(ScrollViewer);
    
    /// <summary>
    /// 距离底部多少距离时提前触发。
    /// 单位是 Avalonia DIP。
    /// </summary>
    public static readonly StyledProperty<double>
        LoadMoreThresholdProperty =
            AvaloniaProperty.Register<
                LoadMoreScrollViewer,
                double>(
                nameof(LoadMoreThreshold),
                defaultValue: 0);

    /// <summary>
    /// 触底时执行的命令。
    /// </summary>
    public static readonly StyledProperty<ICommand?>
        LoadMoreCommandProperty =
            AvaloniaProperty.Register<
                LoadMoreScrollViewer,
                ICommand?>(
                nameof(LoadMoreCommand));

    /// <summary>
    /// 命令参数。
    /// </summary>
    public static readonly StyledProperty<object?>
        LoadMoreCommandParameterProperty =
            AvaloniaProperty.Register<
                LoadMoreScrollViewer,
                object?>(
                nameof(LoadMoreCommandParameter));

    /// <summary>
    /// 是否启用触底加载。
    /// 可以绑定 HasMore。
    /// </summary>
    public static readonly StyledProperty<bool>
        IsLoadMoreEnabledProperty =
            AvaloniaProperty.Register<
                LoadMoreScrollViewer,
                bool>(
                nameof(IsLoadMoreEnabled),
                defaultValue: true);

    /// <summary>
    /// 触底事件。
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs>
        LoadMoreRequestedEvent =
            RoutedEvent.Register<
                LoadMoreScrollViewer,
                RoutedEventArgs>(
                nameof(LoadMoreRequested),
                RoutingStrategies.Bubble);

    private bool _isInTriggerZone;
    private bool _checkQueued;

    public LoadMoreScrollViewer()
    {
        ScrollChanged += OnScrollChanged;

        SizeChanged += (_, _) =>
            QueueCheck();

        Loaded += (_, _) =>
            QueueCheck();
    }

    public double LoadMoreThreshold
    {
        get => GetValue(LoadMoreThresholdProperty);
        set => SetValue(LoadMoreThresholdProperty, value);
    }

    public ICommand? LoadMoreCommand
    {
        get => GetValue(LoadMoreCommandProperty);
        set => SetValue(LoadMoreCommandProperty, value);
    }

    public object? LoadMoreCommandParameter
    {
        get => GetValue(LoadMoreCommandParameterProperty);
        set => SetValue(LoadMoreCommandParameterProperty, value);
    }

    public bool IsLoadMoreEnabled
    {
        get => GetValue(IsLoadMoreEnabledProperty);
        set => SetValue(IsLoadMoreEnabledProperty, value);
    }

    public event EventHandler<RoutedEventArgs>?
        LoadMoreRequested
    {
        add => AddHandler(
            LoadMoreRequestedEvent,
            value);

        remove => RemoveHandler(
            LoadMoreRequestedEvent,
            value);
    }

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 控件第一次完成布局后检查。
        // 如果内容本身没有填满 ScrollViewer，也会触发一次。
        QueueCheck();
    }

    protected override void OnPropertyChanged(
        AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == OffsetProperty ||
            change.Property == ExtentProperty ||
            change.Property == ViewportProperty ||
            change.Property == LoadMoreThresholdProperty ||
            change.Property == IsLoadMoreEnabledProperty)
        {
            if (!IsLoadMoreEnabled)
            {
                _isInTriggerZone = false;
            }

            QueueCheck();
        }
    }

    private void OnScrollChanged(
        object? sender,
        ScrollChangedEventArgs e)
    {
        QueueCheck();
    }

    /// <summary>
    /// 将同一轮布局产生的多个变化合并为一次检查。
    /// </summary>
    private void QueueCheck()
    {
        if (_checkQueued)
        {
            return;
        }

        _checkQueued = true;

        Dispatcher.UIThread.Post(
            () =>
            {
                _checkQueued = false;
                CheckLoadMore();
            },
            DispatcherPriority.Loaded);
    }

    private void CheckLoadMore()
    {
        Debug.WriteLine(
            $"Extent={Extent.Height}, " +
            $"Viewport={Viewport.Height}, " +
            $"Offset={Offset.Y}");
        if (!IsLoadMoreEnabled ||
            Content is null ||
            Viewport.Height <= 0 ||
            Extent.Height <= 0)
        {
            _isInTriggerZone = false;
            return;
        }

        double threshold =
            Math.Max(0, LoadMoreThreshold);

        // 剩余距离 =
        // 内容总高度 - 当前滚动距离 - 可视区域高度
        double remainingDistance =
            Extent.Height -
            Offset.Y -
            Viewport.Height;

        bool isInTriggerZone =
            remainingDistance <= threshold;

        if (!isInTriggerZone)
        {
            // 用户离开触发区域，允许下次再次触发。
            _isInTriggerZone = false;
            return;
        }

        // 停留在底部时不重复触发。
        if (_isInTriggerZone)
        {
            return;
        }

        _isInTriggerZone = true;

        RaiseEvent(
            new RoutedEventArgs(
                LoadMoreRequestedEvent));

        ICommand? command = LoadMoreCommand;
        object? parameter = LoadMoreCommandParameter;

        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
        
    }

    /// <summary>
    /// 手动重置触发状态。
    /// 如果重置时仍位于底部，会再次触发。
    /// </summary>
    public void ResetLoadMoreTrigger()
    {
        _isInTriggerZone = false;
        QueueCheck();
    }
}