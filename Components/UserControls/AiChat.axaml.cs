using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using drive_desktop.ViewModels;
using Ursa.Controls;
namespace drive_desktop.Components.UserControls;

public partial class AiChat : UserControl
{
    private readonly DispatcherTimer _aiLongPressTimer;
    private IPointer? _aiDragPointer;
    private Point _aiDragOffset;
    private bool _isAiPointerPressed;
    private bool _isAiDragging;
    private bool _isAiAssistantUserPositioned;
    private bool _isAiChatOpen;
    private int _aiChatAnimationVersion;
    private Point? _aiChatButtonPosition;

    private const double AiChatExpandedHeight = 600;
    private const int AiChatAnimationDurationMilliseconds = 260;
    public AiChat()
    {
        InitializeComponent();
        _aiLongPressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _aiLongPressTimer.Tick += AiLongPressTimer_Tick;
        AiAssistantHost.SizeChanged += AiAssistantHost_SizeChanged;
       
    }
   



   

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        Dispatcher.UIThread.Post(() =>
        {
            if (!_isAiAssistantUserPositioned)
            {
                PositionAiAssistantAtBottomRight();
            }
        }, DispatcherPriority.Render);
    }

    private void AiFloatingButton_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(AiFloatingButton).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _aiDragPointer = e.Pointer;
        _aiDragOffset = e.GetPosition(AiAssistantHost);
        _isAiPointerPressed = true;
        _isAiDragging = false;
        e.Pointer.Capture(AiFloatingButton);
        _aiLongPressTimer.Start();
        e.Handled = true;
    }

    private void AiLongPressTimer_Tick(object? sender, EventArgs e)
    {
        _aiLongPressTimer.Stop();
        if (_isAiPointerPressed)
        {
            _isAiDragging = true;
        }
    }

    private void AiFloatingButton_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isAiDragging || !ReferenceEquals(e.Pointer, _aiDragPointer))
        {
            return;
        }

        var pointerPosition = e.GetPosition(AiAssistantCanvas);
        var left = Math.Clamp(pointerPosition.X - _aiDragOffset.X, 0,
            Math.Max(0, AiAssistantCanvas.Bounds.Width - AiAssistantHost.Bounds.Width));
        var top = Math.Clamp(pointerPosition.Y - _aiDragOffset.Y, 0,
            Math.Max(0, AiAssistantCanvas.Bounds.Height - AiAssistantHost.Bounds.Height));

        Canvas.SetRight(AiAssistantHost, double.NaN);
        Canvas.SetBottom(AiAssistantHost, double.NaN);
        Canvas.SetLeft(AiAssistantHost, left);
        Canvas.SetTop(AiAssistantHost, top);
        _isAiAssistantUserPositioned = true;
        e.Handled = true;
    }

    private void AiFloatingButton_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _aiDragPointer))
        {
            return;
        }

        var shouldToggleChat = _isAiPointerPressed && !_isAiDragging;
        ResetAiPointerState();

        if (shouldToggleChat)
        {
            ToggleAiChat();
        }

        e.Handled = true;
    }

    private void AiFloatingButton_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ResetAiPointerState();
    }

    private void ResetAiPointerState()
    {
        _aiLongPressTimer.Stop();
        _isAiPointerPressed = false;
        _isAiDragging = false;
        _aiDragPointer?.Capture(null);
        _aiDragPointer = null;
    }

    private async void ToggleAiChat()
    {
        _aiChatButtonPosition = AiFloatingButton.TranslatePoint(default, AiAssistantCanvas);
        var animationVersion = ++_aiChatAnimationVersion;
        _isAiChatOpen = !_isAiChatOpen;

        if (_isAiChatOpen)
        {
            AiChatPanel.IsVisible = true;
            AiChatPanel.Height = 0;
            AiChatPanel.Opacity = 0;

            Dispatcher.UIThread.Post(() =>
            {
                if (animationVersion != _aiChatAnimationVersion)
                {
                    return;
                }

                AiChatPanel.Height = AiChatExpandedHeight;
                AiChatPanel.Opacity = 1;
                Dispatcher.UIThread.Post(
                    AiChatScrollViewer.ScrollToEnd,
                    DispatcherPriority.Background);
            }, DispatcherPriority.Render);
            return;
        }

        AiChatPanel.Height = 0;
        AiChatPanel.Opacity = 0;

        await Task.Delay(AiChatAnimationDurationMilliseconds);
        if (animationVersion == _aiChatAnimationVersion && !_isAiChatOpen)
        {
            AiChatPanel.IsVisible = false;
        }
    }

    private void AiInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (DataContext is AiChatViewModel viewModel &&
            viewModel.SeedAiChatCommand.CanExecute(null))
        {
            viewModel.SeedAiChatCommand.Execute(null);
        }
    }

    private void AiChatScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y <= 0 || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            scrollViewer.ScrollToEnd,
            DispatcherPriority.Background);
    }

    private void AiAssistantHost_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_aiChatButtonPosition is Point buttonPosition)
        {
            KeepAiButtonAt(buttonPosition);
        }
        else if (!_isAiAssistantUserPositioned)
        {
            PositionAiAssistantAtBottomRight();
        }
    }

    private void PositionAiAssistantAtBottomRight()
    {
        PositionAiAssistant(
            AiAssistantCanvas.Bounds.Width - AiAssistantHost.Bounds.Width - 40,
            AiAssistantCanvas.Bounds.Height - AiAssistantHost.Bounds.Height - 40);
    }

    private void KeepAiButtonAt(Point buttonPosition)
    {
        var buttonOffset = AiFloatingButton.TranslatePoint(default, AiAssistantHost);
        if (buttonOffset is Point offset)
        {
            PositionAiAssistant(buttonPosition.X - offset.X, buttonPosition.Y - offset.Y);
        }
    }

    private void PositionAiAssistant(double left, double top)
    {
        var maxLeft = Math.Max(0, AiAssistantCanvas.Bounds.Width - AiAssistantHost.Bounds.Width);
        var maxTop = Math.Max(0, AiAssistantCanvas.Bounds.Height - AiAssistantHost.Bounds.Height);

        Canvas.SetRight(AiAssistantHost, double.NaN);
        Canvas.SetBottom(AiAssistantHost, double.NaN);
        Canvas.SetLeft(AiAssistantHost, Math.Clamp(left, 0, maxLeft));
        Canvas.SetTop(AiAssistantHost, Math.Clamp(top, 0, maxTop));
    }
}
