using Drive.Plugin.Abi;

namespace Drive.Plugin.SDK.Protocol;

/// <summary>
/// 主程序与运行器共同使用的元数据校验；权限始终由主程序再次检查。
/// </summary>
internal static class PluginMetadata
{
    /// <summary>
    /// 检查插件协议范围、标识、展示字段及权限位。
    /// </summary>
    /// <param name="info">通过管道复制的插件元数据。</param>
    internal static void Validate(PluginInfo info)
    {
        if (info.MinSdkVersion > AbiVersions.Sdk || info.MaxSdkVersion < AbiVersions.Sdk)
            throw new PluginException(PluginError.IncompatibleVersion, "插件需要不同版本的 SDK，请使用 2.0 SDK 重新生成。");
        if (string.IsNullOrWhiteSpace(info.Id) || info.Id.Length > 128 || !char.IsAsciiLetterOrDigit(info.Id[0]) ||
            info.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-' && c != '_') ||
            string.IsNullOrWhiteSpace(info.Name) || info.Name.Length > 128 || !Version.TryParse(info.Version, out _) ||
            info.Author is null || info.Author.Length > 1024 || info.Description is null || info.Description.Length > 16384 ||
            (info.Permissions & ~PluginPermission.AllSupported) != 0 || (info.Capabilities & ~(PluginCapabilities)31) != 0 ||
            (uint)info.Kind > 2 || ((info.Capabilities & PluginCapabilities.HasUi) != 0 && (info.Permissions & PluginPermission.UiApplication) == 0) ||
            info.Tags is null || info.Tags.Length > 8 || info.Tags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 24) ||
            info.BackgroundColor is null || !System.Text.RegularExpressions.Regex.IsMatch(info.BackgroundColor, "^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$") ||
            info.IconIco is null || info.IconPng is null || info.IconIco.Length > AbiVersions.MaxIconBytes || info.IconPng.Length > AbiVersions.MaxIconBytes ||
            (info.IconIco.Length > 0 && (info.IconIco.Length < 6 || info.IconIco[0] != 0 || info.IconIco[1] != 0 || info.IconIco[2] != 1 || info.IconIco[3] != 0)))
            throw new PluginException(PluginError.InvalidArgument, "插件元数据或权限声明无效。");
    }
}
