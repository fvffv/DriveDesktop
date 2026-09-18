using Drive.Plugin.Abi;
using Drive.Plugin.SDK;
using Drive.Plugin.SDK.Protocol;

namespace Drive.Plugin.Hosting;

public sealed record PluginCallContext(PluginInfo Info, string DataDirectory);

public interface IPluginHostAdapter
{
    Task<HostResponse> InvokeAsync(PluginCallContext context, HostOperation operation, HostRequest request, CancellationToken cancellationToken);
    void OnPluginDisabled(string pluginId);
}

public enum PluginState { Discovered, AwaitingApproval, Initializing, Enabled, Disabled, Faulted, Incompatible }

public sealed class PluginDescriptor(string libraryPath)
{
    /// <summary>
    /// 本会话启动的独立运行器进程 ID；元数据加载失败或进程已结束时为 null。
    /// </summary>
    public int? RunnerProcessId { get; internal set; }
    public string LibraryPath { get; } = libraryPath;
    public string FileHash { get; internal set; } = "";
    public PluginInfo? Info { get; internal set; }
    public PluginState State { get; internal set; } = PluginState.Discovered;
    public string Error { get; internal set; } = "";
    public string DisplayName => Info?.Name ?? Path.GetFileName(LibraryPath);
}

public sealed record PluginHostOptions(string PluginDirectory, string DataDirectory)
{
    /// <summary>
    /// 自包含插件运行器的启动文件绝对路径；默认与客户端主程序位于同一目录。
    /// </summary>
    public string RunnerPath { get; init; } = Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "Drive.Plugin.Runner.exe" : "Drive.Plugin.Runner");
    public int MaxPlugins { get; init; } = 32;
    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan LoadTimeout { get; init; } = TimeSpan.FromSeconds(15);
}

public enum PluginLogLevel { Info = 0, Warning = 1, Error = 2, Debug = 3 }

public sealed record PluginLogEntry(long Sequence, DateTimeOffset Time, PluginLogLevel Level, string PluginId, string PluginName, string Message)
{
    public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss.fff");
    public string LevelText => Level switch { PluginLogLevel.Warning => "警告", PluginLogLevel.Error => "错误", PluginLogLevel.Debug => "调试", _ => "信息" };
}

public sealed class PluginLog
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Queue<PluginLogEntry> _entries = new();
    private long _sequence;
    public long Version => Interlocked.Read(ref _sequence);
    public PluginLogEntry[] Snapshot() { lock (_gate) return _entries.ToArray(); }
    public PluginLog(string directory) { Directory.CreateDirectory(directory); _path = Path.Combine(directory, "plugins.log"); }
    public string PathName => _path;
    public void Write(string plugin, string message, PluginLogLevel level = PluginLogLevel.Info, string pluginId = "")
    {
        lock (_gate)
        {
            var safe = message.Replace('\r', ' ').Replace('\n', ' ');
            if (safe.Length > 4096) safe = safe[..4096];
            if (!Enum.IsDefined(level)) level = PluginLogLevel.Info;
            var entry = new PluginLogEntry(Interlocked.Increment(ref _sequence), DateTimeOffset.Now, level, pluginId, plugin, safe);
            _entries.Enqueue(entry);
            while (_entries.Count > 1000) _entries.Dequeue();
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > 2 * 1024 * 1024)
                    File.Move(_path, _path + ".previous", true);
                File.AppendAllText(_path, $"{entry.Time:O} [{level}] [{plugin}] [{pluginId}] {safe}{Environment.NewLine}");
            }
            catch { /* Keep in-memory logs even when the log file is not writable. */ }
        }
    }
}
