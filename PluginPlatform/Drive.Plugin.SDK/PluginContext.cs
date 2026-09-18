using Drive.Plugin.SDK.Interop;

namespace Drive.Plugin.SDK;

/// <summary>
/// 插件访问宿主功能的统一入口，在 <see cref="DrivePlugin.OnLoad"/> 调用前完成初始化。
/// </summary>
/// <remarks>
/// 从 <see cref="DrivePlugin.Drive"/> 获取。返回的数据是插件运行时内的快照，不是宿主的可变对象。
/// 宿主在停用和关闭前撤销业务 API；日志仍可用于清理诊断。
/// 上下文仅存在于当前插件进程；主程序与插件之间只传输序列化数据。
/// </remarks>
public sealed class PluginContext
{
    /// <summary>
    /// 组装当前插件的接口代理和事件集合。
    /// </summary>
    /// <param name="requests">绑定当前插件实例的宿主请求调度器。</param>
    /// <param name="info">由插件导出元数据创建的信息快照。</param>
    internal PluginContext(RequestDispatcher requests, PluginInfo info)
    {
        Info = info;
        Files = new(requests);
        Uploads = new(requests);
        Downloads = new(requests);
        User = new(requests);
        UI = new(requests);
        Storage = new(requests);
        System = new(requests);
        Logger = new(requests);
        Events = new(requests);
    }

    /// <summary>
    /// 当前插件的身份、版本、声明权限、图标和标签等元数据。
    /// </summary>
    public PluginInfo Info { get; }
    /// <summary>
    /// 网盘文件与目录查询、修改、下载链接和小文件读取接口。
    /// </summary>
    public FilesApi Files { get; }
    /// <summary>
    /// 宿主上传队列的创建、查询和控制接口。
    /// </summary>
    public UploadsApi Uploads { get; }
    /// <summary>
    /// 宿主下载保存目录查询，以及下载队列的创建、查询和控制接口。
    /// </summary>
    public DownloadsApi Downloads { get; }
    /// <summary>
    /// 当前账户公开资料与存储容量接口。
    /// </summary>
    public UserApi User { get; }
    /// <summary>
    /// 宿主通知、确认框、菜单、导航和文件预览接口。
    /// </summary>
    public UiApi UI { get; }
    /// <summary>
    /// 按插件 ID 隔离的持久化键值存储；不自动按账户隔离。
    /// </summary>
    public StorageApi Storage { get; }
    /// <summary>
    /// 宿主版本、系统、语言、主题和插件临时目录查询接口。
    /// </summary>
    public SystemApi System { get; }
    /// <summary>
    /// 自动附带当前插件身份和时间的日志接口。
    /// </summary>
    public PluginLogger Logger { get; }
    /// <summary>
    /// 宿主事件集合；可在 OnLoad 中订阅，停用时暂停投递，再启用时恢复订阅。
    /// </summary>
    public PluginEvents Events { get; }
}

/// <summary>
/// 插件入口基类；派生类通过重写生命周期方法和应用入口方法实现插件行为。
/// </summary>
/// <remarks>
/// 插件入口应为带有 DrivePluginAttribute 的公开、非抽象、非泛型顶层类，并提供公开无参构造函数。
/// 每个 DLL 只允许一个插件入口。构造函数执行时 Drive 尚未赋值，使用宿主接口的初始化应放在 OnLoad。
/// 界面插件的以下回调由独立运行器在进程主线程（Avalonia UI 线程）串行执行；无界面插件使用串行后台调度。
/// 不要同步等待宿主异步 API，这会阻塞插件界面并可能触发宿主的调用超时。
/// 异步工作应自行保存任务、处理取消并捕获异常；不要从这些 void 回调直接启动未捕获异常的 async void 操作。
/// </remarks>
public abstract class DrivePlugin
{
    /// <summary>
    /// 宿主功能入口；从 OnLoad 开始可用，在插件构造函数中不可使用。
    /// </summary>
    public PluginContext Drive { get; private set; } = null!;

    /// <summary>
    /// 当前插件的元数据，与 Drive.Info 相同；构造函数阶段不可使用。
    /// </summary>
    public PluginInfo Info
    {
        get
        {
            return Drive.Info;
        }
    }

    /// <summary>
    /// 宿主首次初始化插件时调用，用于一次性的对象创建和事件订阅。
    /// </summary>
    /// <remarks>
    /// 扫描 DLL 元数据时不会调用本方法；通常在用户首次启用插件时调用一次。
    /// 调用前 Drive 已可用。后续停用再启用不会重新调用；适合在这里订阅 Drive.Events，避免反复启用时重复绑定。
    /// 不要同步等待宿主 API。
    /// </remarks>
    protected virtual void OnLoad()
    {
    }

    /// <summary>
    /// 每次启用插件时调用，用于启动本轮后台任务、创建取消令牌及重新注册菜单。
    /// </summary>
    /// <remarks>
    /// 首次调用发生在 OnLoad 之后；停用后再启用还会再次调用。
    /// 已有宿主事件订阅会先恢复。菜单在停用时已被移除，需在每次启用时重新注册。
    /// 宿主允许业务 API，但不要在此同步等待异步请求；界面可直接在当前回调中创建。
    /// </remarks>
    protected virtual void OnEnable()
    {
    }

    /// <summary>
    /// 插件停用时调用，用于取消本轮工作并关闭插件窗口。
    /// </summary>
    /// <remarks>
    /// 调用前业务 API 已撤销、未完成请求已取消、事件投递已暂停，不能在此请求保存宿主存储或注销菜单。
    /// 必须持久化的数据应在正常运行时及时写入；此处仍可记录日志和释放本地资源。
    /// 停用保留当前运行器进程；保留的事件处理器在下次启用时自动恢复。
    /// </remarks>
    protected virtual void OnDisable()
    {
    }

    /// <summary>
    /// 宿主最终关闭已初始化的插件时调用，用于释放插件生命周期内的本地资源。
    /// </summary>
    /// <remarks>
    /// 正常关闭已启用插件时，宿主先发送 ApplicationStopping，再调用 OnDisable，最后调用本方法。
    /// 业务 API 此时不可用。应终止后台线程和窗口消息循环，不再等待宿主请求。
    /// 进程崩溃、强制退出或插件被隔离时不保证调用；不要把唯一的数据保存逻辑放在这里。
    /// </remarks>
    protected virtual void OnShutdown()
    {
    }

    /// <summary>
    /// 用户点击插件应用卡片的 OpenPluginViewButton 时调用，用于打开插件窗口或执行应用入口逻辑。
    /// </summary>
    /// <remarks>
    /// 仅在插件启用并声明界面能力及 UiApplication 权限时可用；每次点击都可能触发。
    /// 本回调与注册菜单的 ActionInvoked 事件不同。重写时应复用已打开的窗口，避免重复创建。
    /// 界面插件运行于自己的 UI 线程，可直接创建、显示和关闭 Avalonia Window，也可使用 PluginWindowHost。
    /// </remarks>
    protected virtual void OnAppActivated()
    {
    }

    /// <summary>
    /// 保存已建立的宿主上下文，然后调用一次性初始化回调。
    /// </summary>
    /// <param name="context">本插件专用的宿主上下文。</param>
    internal void Load(PluginContext context)
    {
        Drive = context;
        OnLoad();
    }

    /// <summary>
    /// 由独立运行器进入启用回调；权限和事件的恢复由桥接层负责。
    /// </summary>
    internal void Enable()
    {
        OnEnable();
    }

    /// <summary>
    /// 由独立运行器进入停用回调；业务请求在进入前已暂停。
    /// </summary>
    internal void Disable()
    {
        OnDisable();
    }

    /// <summary>
    /// 由独立运行器进入最终资源清理回调。
    /// </summary>
    internal void Shutdown()
    {
        OnShutdown();
    }

    /// <summary>
    /// 由独立运行器分发应用卡片入口点击。
    /// </summary>
    internal void Activate()
    {
        OnAppActivated();
    }
}
