using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Drive.Plugin.Abi;
using Drive.Plugin.SDK;
using Drive.Plugin.SDK.Protocol;

namespace Drive.Plugin.Hosting;

public sealed class PluginManager
{
    private readonly PluginHostOptions _options;
    private readonly IPluginHostAdapter _adapter;
    private readonly ConcurrentDictionary<string, PluginSession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly SemaphoreSlim _grantsGate = new(1, 1);
    private readonly string _grantPath;
    private volatile bool _stopping;
    private Dictionary<string, PluginGrant> _grants = new(StringComparer.Ordinal);
    public PluginLog Log { get; }
    public event EventHandler? Changed;
    public PluginDescriptor[] Plugins => _sessions.Values.Select(s => s.Descriptor).OrderBy(d => d.DisplayName, StringComparer.Ordinal).ToArray();
    public string PluginDirectory => _options.PluginDirectory;

    public PluginManager(PluginHostOptions options, IPluginHostAdapter adapter)
    {
        _options = options; _adapter = adapter;
        Directory.CreateDirectory(options.PluginDirectory); Directory.CreateDirectory(options.DataDirectory);
        Log = new(options.DataDirectory); _grantPath = Path.Combine(options.DataDirectory, "grants.json");
        try
        {
            if (File.Exists(_grantPath)) _grants = JsonSerializer.Deserialize(File.ReadAllText(_grantPath), HostingJsonContext.Default.DictionaryStringPluginGrant) ?? new(StringComparer.Ordinal);
        }
        catch (Exception e) { Log.Write("host", "Cannot read plugin grants: " + e.Message); }
    }
    public async Task ScanAsync()
    {
        await _scanGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopping) return;
            // 主程序仅检查元数据；依赖 DLL 不会被启动为插件，也不会在扫描阶段执行代码。
            var paths = FindPluginLibraries(_options.PluginDirectory);
            foreach (var directory in Directory.EnumerateDirectories(_options.PluginDirectory))
            {
                var candidates = FindPluginLibraries(directory);
                if (candidates.Count > 1)
                {
                    RecordDiscoveryError(directory, "一个插件文件夹中发现多个带 [DrivePlugin] 的 DLL：" +
                        string.Join("、", candidates.Select(Path.GetFileName)) + "。请将不同插件放入各自目录。");
                    continue;
                }
                paths.AddRange(candidates);
            }
            foreach (var path in paths.Order(StringComparer.Ordinal).Take(_options.MaxPlugins))
            {
                if (_stopping) break;
                var fullPath = Path.GetFullPath(path);
                if (_sessions.ContainsKey(fullPath)) continue;
                var descriptor = new PluginDescriptor(fullPath);
                var session = new PluginSession(descriptor, _options, _adapter, Log, Notify);
                if (!_sessions.TryAdd(fullPath, session)) continue;
                try
                {
                    await using (var stream = File.OpenRead(fullPath))
                        descriptor.FileHash = Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(false));
                    await session.LoadAsync().ConfigureAwait(false);
                    if (descriptor.Info is null || descriptor.State is PluginState.Faulted or PluginState.Incompatible) continue;
                    if (_sessions.Values.Any(s => s != session && s.Descriptor.Info?.Id == descriptor.Info.Id))
                    {
                        session.Quarantine("插件 ID 重复，请只保留一个版本并重启客户端。"); continue;
                    }
                    PluginGrant? grant;
                    await _grantsGate.WaitAsync().ConfigureAwait(false);
                    try { _grants.TryGetValue(descriptor.Info.Id, out grant); }
                    finally { _grantsGate.Release(); }
                    if (!_stopping && grant is not null && grant.Enabled && grant.Hash == descriptor.FileHash && grant.Permissions == descriptor.Info.Permissions)
                        await session.EnableAsync().ConfigureAwait(false);
                }
                catch (Exception e) { session.Quarantine(e.Message); }
            }
            Notify();
        }
        finally { _scanGate.Release(); }
    }

    /// <summary>
    /// 读取一层目录中每个 DLL 的元数据，仅返回确实包含插件特性的主程序集。
    /// </summary>
    /// <param name="directory">Plugins 根目录或一个插件包目录。</param>
    /// <returns>插件主 DLL 路径，不包含普通托管依赖或原生依赖。</returns>
    private List<string> FindPluginLibraries(string directory)
    {
        var result = new List<string>();
        foreach (var path in Directory.EnumerateFiles(directory).Where(p =>
            string.Equals(Path.GetExtension(p), ".dll", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (PluginAssemblyInspector.FindEntryTypes(path).Length > 0) result.Add(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Log.Write("host", "无法检查插件文件 " + path + "：" + error.Message, PluginLogLevel.Warning);
            }
        }
        return result;
    }

    /// <summary>
    /// 在插件列表中展示有歧义的安装目录，且不启动其中任意 DLL。
    /// </summary>
    /// <param name="path">存在打包问题的插件目录。</param>
    /// <param name="error">可供用户修复的具体说明。</param>
    private void RecordDiscoveryError(string path, string error)
    {
        var descriptor = new PluginDescriptor(Path.GetFullPath(path)) { State = PluginState.Faulted, Error = error };
        _sessions.TryAdd(descriptor.LibraryPath, new(descriptor, _options, _adapter, Log, Notify));
        Log.Write(descriptor.DisplayName, error, PluginLogLevel.Error);
    }

    // Called by the application only after the user clicks the explicit grant/enable control.
    public async Task ApproveAndEnableAsync(PluginDescriptor descriptor)
    {
        if (_stopping) throw new PluginException(PluginError.NotEnabled, "Client is shutting down.");
        var session = Resolve(descriptor);
        if (descriptor.Info is null) throw new PluginException(PluginError.Failed, "Plugin metadata is unavailable.");
        await session.EnableAsync().ConfigureAwait(false);
        await SaveGrantAsync(descriptor, true).ConfigureAwait(false);
    }
    public async Task DisableAsync(PluginDescriptor descriptor)
    {
        // Persist disabled even if user code's Disable later times out.
        await SaveGrantAsync(descriptor, false).ConfigureAwait(false);
        await Resolve(descriptor).DisableAsync().ConfigureAwait(false);
    }
    public Task ShowUiAsync(PluginDescriptor descriptor) => Resolve(descriptor).ShowUiAsync();
    public void Publish(DriveEvent data)
    {
        foreach (var session in _sessions.Values) session.Publish(data);
    }
    public void PublishTo(string pluginId, DriveEvent data)
    {
        foreach (var session in _sessions.Values.Where(s => s.Descriptor.Info?.Id == pluginId)) session.Publish(data);
    }
    public void CancelUserRequests() { foreach (var session in _sessions.Values) session.CancelUserRequests(); }
    public async Task ShutdownAsync()
    {
        _stopping = true;
        await _scanGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_sessions.Values.Select(async session =>
            {
                try { await session.ShutdownAsync().ConfigureAwait(false); }
                catch (Exception e) { Log.Write(session.Descriptor.DisplayName, e.Message, PluginLogLevel.Error); }
            })).ConfigureAwait(false);
        }
        finally { _scanGate.Release(); }
    }
    private PluginSession Resolve(PluginDescriptor descriptor) =>
        _sessions.TryGetValue(descriptor.LibraryPath, out var session) && ReferenceEquals(session.Descriptor, descriptor)
            ? session : throw new ArgumentException("Unknown plugin descriptor.");
    private async Task SaveGrantAsync(PluginDescriptor descriptor, bool enabled)
    {
        if (descriptor.Info is null) return;
        await _grantsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _grants[descriptor.Info.Id] = new(descriptor.FileHash, descriptor.Info.Permissions, enabled);
            var data = JsonSerializer.Serialize(_grants, HostingJsonContext.Default.DictionaryStringPluginGrant);
            await File.WriteAllTextAsync(_grantPath + ".tmp", data).ConfigureAwait(false);
            File.Move(_grantPath + ".tmp", _grantPath, true);
        }
        finally { _grantsGate.Release(); }
    }
    private void Notify()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); } catch (Exception e) { Log.Write("host", "Observer failed: " + e.Message); }
    }
}

internal sealed record PluginGrant(string Hash, PluginPermission Permissions, bool Enabled);
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, PluginGrant>))]
internal partial class HostingJsonContext : JsonSerializerContext;
