using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Drive.Plugin.SDK;

namespace Drive.Plugin.Avalonia;

public sealed partial class PluginWindowHost
{
    /// <summary>获取宿主窗口的当前位置，在其中央创建或激活插件窗口；Windows 还关联原生所有者，使插件窗口保持在宿主前方。</summary>
    /// <param name="createWindow">仅在没有现存窗口时，在插件 UI 线程调用的窗口工厂。</param>
    /// <param name="activateExisting">现存窗口的可选更新回调，在插件 UI 线程执行。</param>
    /// <param name="cancellationToken">取消本次定位和显示；不会关闭已经显示的窗口。</param>
    /// <returns>窗口完成显示与激活的任务，不等待窗口关闭。</returns>
    /// <remarks>使用同版本 SDK、主程序和运行器。无宿主窗口时居中到屏幕。Linux/macOS 使用 Avalonia 定位和激活；Wayland 等环境最终由合成器决定位置和焦点。不会设为全局置顶或合并两个进程的 UI 线程。</remarks>
    public async Task ShowNearHostAsync(Func<Window> createWindow, Action<Window>? activateExisting = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createWindow);
        var version = Interlocked.Increment(ref _visibilityVersion);
        var system = await _context.System.GetInfoAsync(cancellationToken).ConfigureAwait(false);
        await InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_closed || version != Volatile.Read(ref _visibilityVersion)) return;
            if (_window is null)
            {
                _window = createWindow() ?? throw new InvalidOperationException("窗口工厂不能返回 null。");
                _window.Closed += OnWindowClosed;
            }
            else activateExisting?.Invoke(_window);
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.ShowActivated = true;
            PositionWindow(_window, system.Window);
            _window.Show();
            // Show 后窗口句柄及目标显示器的实际缩放才一定可用，再校正一次位置。
            PositionWindow(_window, system.Window);
            if (OperatingSystem.IsWindows()) AssociateWindowsOwner(_window, system.Window);
            _window.Activate();
        });
    }

    /// <summary>按目标显示器缩放计算居中位置，并限制到可用工作区。</summary>
    /// <param name="window">插件窗口。</param>
    /// <param name="host">宿主物理像素边界；可以为空。</param>
    private static void PositionWindow(Window window, HostWindowInfo? host)
    {
        if (host is null || host.Width <= 0 || host.Height <= 0)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var center = new PixelPoint(host.X + host.Width / 2, host.Y + host.Height / 2);
        var screen = window.Screens.ScreenFromPoint(center);
        var scale = screen?.Scaling ?? host.Scaling;
        var width = (int)Math.Round((window.FrameSize?.Width ?? (double.IsFinite(window.Width) ? window.Width : 940)) * scale);
        var height = (int)Math.Round((window.FrameSize?.Height ?? (double.IsFinite(window.Height) ? window.Height : 650)) * scale);
        var x = center.X - width / 2;
        var y = center.Y - height / 2;
        if (screen is not null)
        {
            x = Math.Clamp(x, screen.WorkingArea.X, Math.Max(screen.WorkingArea.X, screen.WorkingArea.Right - width));
            y = Math.Clamp(y, screen.WorkingArea.Y, Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Bottom - height));
        }
        window.Position = new PixelPoint(x, y);
    }

    /// <summary>验证宿主 HWND 所属进程后设置原生所有者，不改变窗口父子类型或 UI 线程。</summary>
    /// <param name="window">已经显示的插件顶层窗口。</param>
    /// <param name="host">经 IPC 返回的宿主窗口信息。</param>
    private static void AssociateWindowsOwner(Window window, HostWindowInfo? host)
    {
        if (host is null || host.WindowsHandle == 0 || host.ProcessId <= 0) return;
        var owner = new IntPtr(host.WindowsHandle);
        GetWindowThreadProcessId(owner, out var processId);
        if (processId != host.ProcessId) return;
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        if (IntPtr.Size == 8) SetWindowLongPtr(handle, -8, owner);
        else SetWindowLong(handle, -8, owner.ToInt32());
        SetForegroundWindow(handle);
    }

    /// <summary>获取窗口所属进程，用于验证 HWND 仍属于宿主。</summary>
    /// <param name="window">原生窗口。</param>
    /// <param name="processId">接收进程 ID。</param>
    /// <returns>创建窗口的线程 ID。</returns>
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>设置 64 位窗口所有者。</summary>
    /// <param name="window">插件窗口。</param>
    /// <param name="index">GWLP_HWNDPARENT。</param>
    /// <param name="value">宿主窗口。</param>
    /// <returns>原来的所有者。</returns>
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    /// <summary>设置 32 位窗口所有者。</summary>
    /// <param name="window">插件窗口。</param>
    /// <param name="index">GWLP_HWNDPARENT。</param>
    /// <param name="value">宿主窗口。</param>
    /// <returns>原来的所有者。</returns>
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    /// <summary>使用宿主授予的前台激活资格激活插件窗口。</summary>
    /// <param name="window">插件窗口。</param>
    /// <returns>系统是否接受激活。</returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
