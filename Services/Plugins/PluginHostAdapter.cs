using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.ViewModels;
using drive_desktop.Views;
using Drive.Plugin.Abi;
using Drive.Plugin.Hosting;
using Drive.Plugin.SDK;
using Drive.Plugin.SDK.Protocol;
using Ursa.Controls;

namespace drive_desktop.Services.Plugins;

public sealed class PluginHostAdapter : IPluginHostAdapter
{
    private readonly WebApiService _web;
    private readonly UserInfoService _user;
    private readonly AppConfigService _config;
    private readonly IThemeService _theme;
    private readonly Func<FileTransmissionService> _transfers;
    private readonly Func<FilePageViewModel> _files;

    public PluginHostAdapter(WebApiService web, UserInfoService user, AppConfigService config, IThemeService theme,
        Func<FileTransmissionService> transfers, Func<FilePageViewModel> files)
    { _web = web; _user = user; _config = config; _theme = theme; _transfers = transfers; _files = files; }

    public async Task<HostResponse> InvokeAsync(PluginCallContext context, HostOperation operation, HostRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 客户端本地设置不依赖账户；在 UI 线程读取设置页正在使用的同一份配置。
        if (operation == HostOperation.DownloadSaveDirectory)
        {
            return new() { Text = await OnUiAsync(() => _config.Config.DownloadLocation ?? "", cancellationToken) };
        }
        // Capture one user's identity. Never apply an old plugin request to a newly logged-in account.
        var userId = await OnUiAsync(() => _user.ShowUserInfo.UserId, cancellationToken);
        bool requiresLogin = operation is >= HostOperation.FileList and <= HostOperation.DownloadCancel ||
                             operation is HostOperation.UserStorage or HostOperation.UiNavigate or HostOperation.UiOpenFile;
        if (requiresLogin && userId == Guid.Empty) throw new PluginException(PluginError.NotLoggedIn, "请先登录网盘。");
        void CheckUser()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (requiresLogin && _user.ShowUserInfo.UserId != userId)
                throw new PluginException(PluginError.Cancelled, "当前账户已经改变，请重新发起请求。");
        }
        CheckUser();
        string Folder() => string.IsNullOrWhiteSpace(request.FolderId) ? _user.ShowUserInfo.RootFolderId : Identifier(request.FolderId);
        switch (operation)
        {
            case HostOperation.FileList:
            {
                if (request.PageIndex < 1 || request.PageSize is < 1 or > 200) throw Invalid("每页数量必须为 1–200，页码从 1 开始。");
                var folder = Folder();
                var result = Ensure(await _web.FileApi.GetUserDirectoryFileInfoAsync(folder, request.PageIndex, request.PageSize));
                CheckUser();
                return new() { Page = MapPage(result, folder, request.PageIndex, request.PageSize) };
            }
            case HostOperation.FileGet: return new() { File = PluginDtoMapper.File(await GetFileAsync(request.FileId, cancellationToken)) };
            case HostOperation.CurrentFolder: return new() { Text = await OnUiAsync(() => _user.CurrentDirectoryId ?? _user.ShowUserInfo.RootFolderId, cancellationToken) };
            case HostOperation.FileSearch:
            {
                if (request.Query.Length > 1000 || request.Extensions.Length > 32 || request.Extensions.Any(x => x.Length > 32)) throw Invalid("搜索条件过长。");
                var result = Ensure(await _web.FileApi.SearchFilesAsync(new SearchInfoDTO { Keyword = request.Query, FileType = request.Extensions }, request.Semantic));
                CheckUser();
                // The current server search API has no page parameters. Do not silently truncate matches.
                return new() { Page = MapPage(result, "", 1, result.FileInfos?.Length ?? 0) };
            }
            case HostOperation.FolderCreate:
            {
                var id = Ensure(await _web.FileApi.CreateFolderAsync(Folder(), FileName(request.Name)))?.ToString() ?? "";
                await RefreshFilesAsync(cancellationToken);
                return new() { Text = id };
            }
            case HostOperation.FileRename:
                Ensure(await _web.FileApi.RenameFileOrDirAsync(new() { Id = Identifier(request.FileId), NewName = FileName(request.Name), Type = request.IsFolder ? 1 : 0 }));
                await RefreshFilesAsync(cancellationToken); return Success();
            case HostOperation.FileMove:
                ValidateIds(request);
                Ensure(await _web.FileApi.MoveFileOrDirAsync(new() { FileIds = request.FileIds, FolderIds = request.FolderIds, NewFolderId = Folder() }));
                await RefreshFilesAsync(cancellationToken); return Success();
            case HostOperation.FileDelete:
            {
                ValidateIds(request);
                using var deletionBatch = PluginEventHub.BeginDeletionBatch();
                if (request.FileIds.Length > 0) Ensure(await _web.FileApi.DeleteUserFileAsync(request.FileIds));
                CheckUser();
                if (request.FolderIds.Length > 0) Ensure(await _web.FileApi.DeleteUserFolderAsync(request.FolderIds));
                await RefreshFilesAsync(cancellationToken); return Success();
            }
            case HostOperation.FileCopy:
                Ensure(await _web.FileApi.SaveToFileAsync(Folder(), Identifier(request.FileId), null!));
                await RefreshFilesAsync(cancellationToken); return Success();
            case HostOperation.FileDownloadUrl:
            {
                var key = Ensure(await _web.FileApi.GetFileDownLoadTempKeyAsync(Identifier(request.FileId)))?.ToString();
                if (string.IsNullOrWhiteSpace(key)) throw new PluginException(PluginError.Failed, "服务器未返回临时下载密钥。");
                return new() { Text = _config.Config.ServerIp.TrimEnd('/') + "/api/Files/DownLoadKey/" + Uri.EscapeDataString(key) };
            }
            case HostOperation.FileRead:
            {
                if (request.MaxBytes is < 1 or > 4 * 1024 * 1024) throw Invalid("单次读取限制为 1 字节至 4 MiB，大文件请调用下载 API。");
                var file = await GetFileAsync(request.FileId, cancellationToken);
                if (file.FileSizeInBytes > (ulong)request.MaxBytes) throw new PluginException(PluginError.TooLarge, "文件超过读取上限。");
                CheckUser();
                var key = Ensure(await _web.FileApi.GetFileDownLoadTempKeyAsync(Identifier(request.FileId)))?.ToString();
                using var stream = await _web.FileApi.DownLoadKey(key!);
                using var result = new MemoryStream();
                var buffer = new byte[64 * 1024];
                int count;
                while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    if (result.Length + count > request.MaxBytes) throw new PluginException(PluginError.TooLarge, "文件流超过读取上限。");
                    result.Write(buffer, 0, count);
                }
                return new() { Data = result.ToArray() };
            }
            case HostOperation.UploadStart:
            {
                var path = Path.GetFullPath(request.LocalPath);
                if (!File.Exists(path)) throw new PluginException(PluginError.NotFound, "上传文件不存在。");
                var folder = Folder();
                return new() { Text = await OnUiTaskAsync(async () =>
                {
                    CheckUser();
                    return (await _transfers().EnqueuePluginUploadAsync(path, folder, userId, cancellationToken)).ToString();
                }, cancellationToken) };
            }
            case HostOperation.DownloadStart:
            {
                var file = await GetFileAsync(request.FileId, cancellationToken);
                // Do not let a server-supplied filename escape the configured download directory.
                FileName(file.FileName);
                return new() { Text = await OnUiTaskAsync(async () =>
                {
                    CheckUser();
                    return (await _transfers().EnqueuePluginDownloadAsync(file, userId, cancellationToken)).ToString();
                }, cancellationToken) };
            }
            case HostOperation.UploadList:
                return new() { Tasks = await OnUiAsync(() => _transfers().FileUploadInfos.Where(x => x.Uid == userId).Select(x => PluginDtoMapper.Upload(x)).ToArray(), cancellationToken) };
            case HostOperation.DownloadList:
                return new() { Tasks = await OnUiAsync(() => _transfers().FileDownloadInfos.Where(x => x.Uid == userId).Select(x => PluginDtoMapper.Download(x)).ToArray(), cancellationToken) };
            case HostOperation.UploadPause: case HostOperation.UploadResume: case HostOperation.UploadCancel:
            case HostOperation.DownloadPause: case HostOperation.DownloadResume: case HostOperation.DownloadCancel:
                await OnUiTaskAsync(async () => { CheckUser(); await ControlTransferAsync(operation, request.TaskId, userId); return true; }, cancellationToken);
                return Success();
            case HostOperation.UserCurrent: return new() { User = await OnUiAsync(CurrentUser, cancellationToken) };
            case HostOperation.UserStorage:
            {
                var capacity = Ensure(await _web.FileApi.GetUserStorageCapacityInfoAsync());
                return new() { Capacity = new(capacity.UsedSpaceInBytes, capacity.TotalSpaceInBytes) };
            }
            case HostOperation.UiNotify:
                ValidateText(request);
                await OnUiAsync(() =>
                {
                    var manager = Home.GlobalToastManager ?? MainWindow.GlobalToastManager;
                    if (manager is null) throw new PluginException(PluginError.NotFound, "当前没有可显示通知的窗口。");
                    manager.Show(new Toast($"{context.Info.Name} · {request.Title}\n{request.Text}"), type: NotificationType.Information);
                    return true;
                }, cancellationToken);
                return Success();
            case HostOperation.UiConfirm:
                ValidateText(request);
                return new() { Result = await OnUiTaskAsync(async () =>
                {
                    if (Home.GlobalToastManager is null) throw new PluginException(PluginError.NotLoggedIn, "请先打开网盘主窗口。");
                    return await OverlayMessageBox.ShowAsync(request.Text, $"{context.Info.Name} · {request.Title}",
                        icon: MessageBoxIcon.Question, button: MessageBoxButton.OKCancel) == MessageBoxResult.OK;
                }, cancellationToken) };
            case HostOperation.UiRegisterAction:
                await OnUiAsync(() => { PluginUiRegistry.Shared.Register(context.Info, request.Action ?? throw Invalid("缺少菜单项。")); return true; }, cancellationToken);
                return Success();
            case HostOperation.UiUnregisterAction:
                await OnUiAsync(() => { PluginUiRegistry.Shared.Unregister(context.Info.Id, request.Key); return true; }, cancellationToken);
                return Success();
            case HostOperation.UiNavigate:
                if (!Enum.IsDefined(request.Page) || request.Text.Length > 1024) throw Invalid("无效页面或路径。");
                await OnUiAsync(() =>
                {
                    CheckUser();
                    string[] titles = ["我的文件", "搜索", "传输管理", "分享管理", "统计看板", "设置", "插件应用"];
                    WeakReferenceMessenger.Default.Send(new SidebarItemMessage((int)request.Page, titles[(int)request.Page]));
                    if (request.Page == HostPage.Files && request.Text.Length > 0) WeakReferenceMessenger.Default.Send(new FilePageMessage(request.Text));
                    return true;
                }, cancellationToken); return Success();
            case HostOperation.UiOpenFile:
            {
                var file = await GetFileAsync(request.FileId, cancellationToken);
                await OnUiTaskAsync(async () => { CheckUser(); await _files().OpenFileCommand.ExecuteAsync(file); return true; }, cancellationToken);
                return Success();
            }
            case HostOperation.SystemInfo:
            {
                var directory = Path.Combine(context.DataDirectory, "temp"); Directory.CreateDirectory(directory);
                return new() { System = await OnUiAsync(() => new HostSystemInfo
                {
                    HostVersion = typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                    OperatingSystem = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    Language = CultureInfo.CurrentUICulture.Name, Theme = _theme.CurrentTheme == ThemeVariant.Dark ? "dark" : "light", PluginTempDirectory = directory,
                    Window = GetHostWindowInfo()
                }, cancellationToken) };
            }
            default: throw new PluginException(PluginError.Unsupported, "未实现的宿主操作。");
        }
    }

    public void OnPluginDisabled(string pluginId) => Dispatcher.UIThread.Post(() => PluginUiRegistry.Shared.RemovePlugin(pluginId));
    /// <summary>在 UI 线程获取可见主窗口的物理边界；优先使用网盘 Home，再使用当前活动窗口。</summary>
    private static HostWindowInfo? GetHostWindowInfo()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) return null;
        var window = desktop.Windows.FirstOrDefault(x => x is Home && x.IsVisible)
            ?? desktop.Windows.FirstOrDefault(x => x.IsActive && x.IsVisible)
            ?? desktop.Windows.FirstOrDefault(x => x.IsVisible);
        if (window is null) return null;
        var scale = window.RenderScaling;
        var size = window.FrameSize ?? window.Bounds.Size;
        return new HostWindowInfo
        {
            X = window.Position.X, Y = window.Position.Y,
            Width = (int)Math.Round(size.Width * scale), Height = (int)Math.Round(size.Height * scale),
            Scaling = scale, ProcessId = Environment.ProcessId,
            WindowsHandle = OperatingSystem.IsWindows() ? window.TryGetPlatformHandle()?.Handle.ToInt64() ?? 0 : 0
        };
    }
    /// <summary>切换登录账户时使文件事件中缓存的分享关联失效。</summary>
    public void OnAccountChanged()
    {
        if (_web.FileApi is PluginFileApiDecorator decorator) decorator.ResetAccount();
    }
    public UserInfo CurrentUser() => new()
    {
        IsLoggedIn = _user.ShowUserInfo.UserId != Guid.Empty, Id = _user.ShowUserInfo.UserId == Guid.Empty ? "" : _user.ShowUserInfo.UserId.ToString(),
        UserName = _user.ShowUserInfo.UserName ?? "", Nickname = _user.ShowUserInfo.Nickname ?? "", RootFolderId = _user.ShowUserInfo.RootFolderId ?? ""
    };
    private async Task<UserFilesInfoItem> GetFileAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = Ensure(await _web.FileApi.GetUserFileInfo(Identifier(id)));
        if (data is not JsonElement json) throw new PluginException(PluginError.Failed, "服务器返回的文件信息格式不正确。");
        var file = JsonSerializer.Deserialize(json.GetRawText(), AppConfigJsonContext.Default.UserFilesInfoItem);
        return file is not null && !string.IsNullOrEmpty(file.Id) ? file : throw new PluginException(PluginError.NotFound, "文件不存在。");
    }
    private async Task ControlTransferAsync(HostOperation operation, string id, Guid userId)
    {
        if (!Guid.TryParse(id, out var taskId)) throw Invalid("无效任务 ID。");
        var service = _transfers();
        if (operation is HostOperation.UploadPause or HostOperation.UploadResume or HostOperation.UploadCancel)
        {
            var item = service.FileUploadInfos.FirstOrDefault(x => x.Id == taskId && x.Uid == userId) ?? throw new PluginException(PluginError.NotFound, "上传任务不存在。");
            if (operation == HostOperation.UploadCancel) await service.UploadCancelAsync(item);
            else if (item.IsPause != (operation == HostOperation.UploadPause)) await service.PauseOrResumeUpload(item);
        }
        else
        {
            var item = service.FileDownloadInfos.FirstOrDefault(x => x.Id == taskId && x.Uid == userId) ?? throw new PluginException(PluginError.NotFound, "下载任务不存在。");
            if (operation == HostOperation.DownloadCancel) await service.DownloadCancelAsync(item);
            else if (item.IsPause != (operation == HostOperation.DownloadPause)) await service.PauseOrResume(item);
        }
    }
    private Task<bool> RefreshFilesAsync(CancellationToken cancellationToken) => OnUiTaskAsync(async () =>
    {
        if (!string.IsNullOrWhiteSpace(_user.CurrentDirectoryId)) await _files().RefreshCurrentDirectoryCommand.ExecuteAsync(null);
        return true;
    }, cancellationToken);
    private static FilePage MapPage(UserFilesInfo page, string parent, int index, int size) => new()
    {
        Files = (page.FileInfos ?? []).Select(PluginDtoMapper.File).ToArray(),
        Folders = (page.Dirs ?? []).Where(x => x.Id != parent).Select(x => PluginDtoMapper.Folder(x, parent)).ToArray(),
        TotalFiles = page.TotalFileCount, PageIndex = index, PageSize = size
    };
    private static T Ensure<T>(DefaultMsg<T> response) => response.Status == 0 ? response.Data : throw new PluginException(PluginError.Failed, response.Msg ?? "服务器操作失败。");
    private static HostResponse Success() => new() { Result = true };
    private static PluginException Invalid(string message) => new(PluginError.InvalidArgument, message);
    private static string Identifier(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 256 ? id : throw Invalid("无效文件或目录 ID。");
    private static string FileName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 255 && name is not "." and not ".." &&
        name.IndexOfAny(['/', '\\', '\0', ':', '*', '?', '"', '<', '>', '|']) < 0 && !name.Any(char.IsControl)
            ? name : throw Invalid("名称不能包含路径分隔符或特殊字符。");
    private static void ValidateIds(HostRequest request)
    {
        if (request.FileIds.Length + request.FolderIds.Length is 0 or > 200) throw Invalid("每次操作需要 1–200 个文件或目录。");
        foreach (var id in request.FileIds.Concat(request.FolderIds)) Identifier(id);
    }
    private static void ValidateText(HostRequest request)
    { if (request.Title.Length > 128 || request.Text.Length > 2000) throw Invalid("提示内容过长。"); }
    private static async Task<T> OnUiAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        return await Dispatcher.UIThread.InvokeAsync(() => { cancellationToken.ThrowIfCancellationRequested(); return action(); });
    }
    private static async Task<T> OnUiTaskAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        return await Dispatcher.UIThread.InvokeAsync(() => { cancellationToken.ThrowIfCancellationRequested(); return action(); });
    }
}
