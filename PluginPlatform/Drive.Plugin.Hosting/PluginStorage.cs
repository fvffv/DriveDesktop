using System.Security.Cryptography;
using System.Text;
using Drive.Plugin.Abi;
using Drive.Plugin.SDK;
using Drive.Plugin.SDK.Protocol;

namespace Drive.Plugin.Hosting;

internal sealed class PluginStorage
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public PluginStorage(string directory) => _directory = Path.Combine(directory, "storage");
    public static string NamespaceDirectory(string root, string pluginId) =>
        Path.Combine(root, "data", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pluginId))).ToLowerInvariant());

    public async Task<HostResponse> InvokeAsync(HostOperation operation, HostRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            if (operation == HostOperation.StorageEnumerate)
            {
                var keys = new List<string>();
                foreach (var file in Directory.EnumerateFiles(_directory, "*.txt").Take(257))
                    try { keys.Add(Encoding.UTF8.GetString(Convert.FromHexString(Path.GetFileNameWithoutExtension(file)))); } catch (FormatException) { }
                return new() { Keys = keys.Order(StringComparer.Ordinal).ToArray() };
            }
            var keyBytes = Encoding.UTF8.GetBytes(request.Key);
            if (keyBytes.Length is 0 or > 64) throw new PluginException(PluginError.InvalidArgument, "Storage key must be 1–64 UTF-8 bytes.");
            var path = Path.Combine(_directory, Convert.ToHexString(keyBytes) + ".txt");
            if (operation == HostOperation.StorageGet)
                return new() { Text = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) : null };
            if (operation == HostOperation.StorageDelete) { File.Delete(path); return new() { Result = true }; }
            var value = request.Value ?? "";
            var bytes = Encoding.UTF8.GetBytes(value);
            var files = new DirectoryInfo(_directory).GetFiles("*.txt");
            long oldSize = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (bytes.Length > 64 * 1024 || (files.Length >= 256 && !File.Exists(path)) || files.Sum(f => f.Length) - oldSize + bytes.Length > 4 * 1024 * 1024)
                throw new PluginException(PluginError.TooLarge, "Plugin storage quota exceeded (256 keys / 64 KiB per value / 4 MiB total).");
            var temporary = path + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return new() { Result = true };
        }
        finally { _gate.Release(); }
    }
}
