using System.Reflection;
using System.Runtime.Loader;
using System.Diagnostics.CodeAnalysis;
using Drive.Plugin.Abi;
using Drive.Plugin.SDK;
using Drive.Plugin.SDK.Protocol;

namespace Drive.Plugin.Runner;

/// <summary>
/// 在普通 .NET 运行器内加载插件私有程序集，同时统一 SDK 与 Avalonia 类型身份。
/// </summary>
internal sealed class PluginLoader : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _directory;
    // 入口类型来自发布后安装的外部 DLL；该 DLL 本身不参与运行器裁剪。
    // 此标注只描述 Activator 的构造函数要求，共享框架的实际保留规则在 Trimming.targets。
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private readonly Type _entry;
    internal PluginInfo Info { get; }

    /// <summary>
    /// 读取普通托管程序集中的声明；不会调用插件入口构造函数或 OnLoad。
    /// </summary>
    /// <param name="path">插件主程序集绝对路径。</param>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "插件 DLL 是运行时传入的外部、未裁剪托管程序集；其依赖由插件自己的 deps.json 和目录解析，不能由运行器链接器静态分析。")]
    internal PluginLoader(string path) : base("Drive plugin: " + Path.GetFileNameWithoutExtension(path), isCollectible: false)
    {
        _directory = Path.GetDirectoryName(path)!;
        _resolver = new(path);
        var entries = PluginAssemblyInspector.FindEntryTypes(path);
        if (entries.Length != 1) throw new InvalidOperationException("一个插件 DLL 必须且只能包含一个 [DrivePlugin] 入口。");
        var assembly = LoadFromAssemblyPath(path);
        // 只解析标记的入口类型，不为发现入口遍历实例化其他 DLL 或其所有类型。
        _entry = FindEntryType(assembly, entries[0]);
        if (!_entry.IsPublic || _entry.IsAbstract || _entry.IsGenericType || _entry.IsNested ||
            !typeof(DrivePlugin).IsAssignableFrom(_entry) || _entry.GetConstructor(Type.EmptyTypes) is null)
            throw new InvalidOperationException("插件入口必须是公开、非抽象、非泛型的 DrivePlugin，并提供公开无参构造函数。");
        var attribute = _entry.GetCustomAttribute<DrivePluginAttribute>()!;
        PluginPermission permissions = PluginPermission.None;
        foreach (var permission in _entry.GetCustomAttributes<PluginPermissionAttribute>()) permissions |= permission.Permission;
        Info = new()
        {
            Id = attribute.Id, Name = attribute.Name, Version = attribute.Version, Author = attribute.Author,
            Description = attribute.Description, MinSdkVersion = attribute.MinSdkVersion, MaxSdkVersion = attribute.MaxSdkVersion,
            Permissions = permissions, Capabilities = attribute.Capabilities, Kind = attribute.Kind,
            IconIco = Convert.FromBase64String(attribute.IconBase64), BackgroundColor = attribute.BackgroundColor, Tags = attribute.Tags
        };
        PluginMetadata.Validate(Info);
    }

    /// <summary>
    /// 从外部未裁剪插件程序集取得入口类型，并把实例化所需的构造函数要求传递给裁剪器。
    /// </summary>
    /// <param name="assembly">刚加载的插件主程序集。</param>
    /// <param name="fullName">元数据检查阶段找到的入口完整名称。</param>
    /// <returns>带公开无参构造函数保留标记的入口类型。</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "入口名称来自外部未裁剪插件 DLL 的 PE 元数据；运行器无法在编译期知道该类型。")]
    [UnconditionalSuppressMessage("Trimming", "IL2073",
        Justification = "Assembly.GetType 的返回值没有 DAM 注解，但这里返回的是外部未裁剪插件的公开无参入口；该要求已在本方法返回值和字段上声明。")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private static Type FindEntryType(Assembly assembly, string fullName)
    {
        return assembly.GetType(fullName, throwOnError: true)!;
    }

    /// <summary>
    /// 首次启用时创建插件入口；构造函数不得调用尚未赋值的 Drive。
    /// </summary>
    /// <returns>新插件入口对象。</returns>
    internal DrivePlugin CreatePlugin()
    {
        return (DrivePlugin)Activator.CreateInstance(_entry)!;
    }

    /// <summary>
    /// 公共框架及 Avalonia 的 D-Bus 依赖始终复用默认上下文，私有依赖通过 deps.json 或插件目录解析。
    /// </summary>
    /// <param name="assemblyName">插件请求的程序集标识。</param>
    /// <returns>共享或私有程序集；系统程序集返回 null 交给默认解析器。</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "插件私有程序集在运行时按 AssemblyDependencyResolver 加载，并由插件项目保证未裁剪；运行器不能提前知道插件类型。")]
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? "";
        if (name == "Avalonia" || name.StartsWith("Avalonia.", StringComparison.Ordinal) ||
            name is "SkiaSharp" or "HarfBuzzSharp" or "MicroCom.Runtime" or "Tmds.DBus.Protocol" or
                "Drive.Plugin.SDK" or "Drive.Plugin.Abi" or "Drive.Plugin.Avalonia")
        {
            var shared = Default.LoadFromAssemblyName(assemblyName);
            var actual = shared.GetName();
            // 同一主/次版本允许向前兼容补丁，例如媒体控件引用 12.1.1、运行器提供 12.1.2。
            // 不允许倒退版本或混用不兼容的 Avalonia/Skia 主次版本。
            if ((assemblyName.Version is { } requested && (actual.Version is null || actual.Version < requested ||
                actual.Version.Major != requested.Major || actual.Version.Minor != requested.Minor)) ||
                !(assemblyName.GetPublicKeyToken() ?? []).SequenceEqual(actual.GetPublicKeyToken() ?? []))
                throw new FileLoadException($"插件引用 {assemblyName}，运行器提供 {shared.GetName()}；请使用配套 SDK 和 Avalonia 版本重新生成。");
            return shared;
        }
        // System.Reactive 等 NuGet 私有依赖也以 System. 开头，不能仅凭前缀交给运行器。
        // 插件目录里不存在的运行时程序集自然返回 null，由默认上下文提供。
        if (name is "netstandard" or "System.Private.CoreLib") return null;
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is null)
        {
            var candidate = Path.Combine(_directory, name + ".dll");
            if (File.Exists(candidate)) path = candidate;
        }
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <summary>
    /// 解析插件自己携带的原生依赖；Avalonia 的共享原生依赖由运行器默认上下文解析。
    /// </summary>
    /// <param name="unmanagedDllName">原生库名称。</param>
    /// <returns>找到的库句柄；未找到时返回零以继续系统解析。</returns>
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? 0 : LoadUnmanagedDllFromPath(path);
    }
}
