using System.Net;
using System.Net.Http.Headers;

namespace ArchivePreviewPlugin.Core;

/// <summary>可寻址的只读 HTTP 流；只接受有效的 206 响应，绝不退化为下载整个文件。</summary>
/// <remarks>归档解码器在后台线程同步读取此流；同一实例由一个归档任务独占。</remarks>
internal sealed class HttpRangeStream : Stream
{
    internal const int BlockSize = 64 * 1024;
    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string>> _getUrl;
    private readonly long _length;
    private readonly string? _etag;
    private readonly DateTimeOffset? _modified;
    private readonly BlockCache _cache;
    private string _url;
    private long _position;
    private long _memoryOffset = -1;
    private byte[]? _memory;
    private bool _disposed;
    internal CancellationToken OperationToken { get; set; }
    /// <summary>分段读取完成后的回调；参数为累计网络字节数和远端文件总长度。</summary>
    internal Action<long, long>? ReadProgress { get; set; }
    internal long NetworkBytes { get; private set; }
    internal long CacheHitBytes { get; private set; }
    internal long NetworkLimit { get; set; } = 16L * 1024 * 1024;
    /// <summary>当前读取块大小；固实归档提取时由会话提升以减少高延迟请求次数。</summary>
    internal int ActiveBlockSize { get; set; } = BlockSize;

    /// <summary>记录经过探测的长度、实体版本及缓存，不执行网络请求。</summary>
    private HttpRangeStream(HttpClient client, Func<CancellationToken, Task<string>> getUrl, string url,
        long length, string? etag, DateTimeOffset? modified, BlockCache cache)
    {
        _client = client; _getUrl = getUrl; _url = url; _length = length; _etag = etag; _modified = modified; _cache = cache;
        NetworkBytes = 1;
    }

    /// <summary>用一个字节的分段请求探测实际长度和版本，按账户、文件信息和实体版本选择缓存。</summary>
    internal static async Task<HttpRangeStream> OpenAsync(HttpClient client, Func<CancellationToken, Task<string>> getUrl,
        string cacheRoot, string identity, bool hasContentHash, long expectedLength, CancellationToken token)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var url = await getUrl(token).ConfigureAwait(false);
            using var request = CreateRequest(url, 0, 0);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (attempt == 0 && IsExpiredLinkResponse(response)) continue;
            var length = ValidateResponse(response, 0, 0, null);
            if (expectedLength > 0 && length != expectedLength) throw new IOException("网盘文件大小已改变，请重新打开压缩包。");
            var etag = response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() : null;
            var modified = response.Content.Headers.LastModified;
            // 没有可靠版本标识时不跨会话复用，避免把不同实体的字节拼在一起。
            var version = etag ?? (hasContentHash ? "content-hash" : Guid.NewGuid().ToString("N"));
            var cache = new BlockCache(cacheRoot, identity + "|" + length + "|" + version);
            using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var probe = new byte[1];
            await body.ReadExactlyAsync(probe, token).ConfigureAwait(false);
            return new HttpRangeStream(client, getUrl, url, length, etag, modified, cache) { OperationToken = token };
        }
        throw new IOException("临时下载链接不可用，请重新打开压缩包。");
    }

    /// <summary>请求恰好覆盖指定分段的数据；链接过期时通过 SDK 重新获取一次。</summary>
    private async Task<byte[]> FetchAsync(long offset, int count)
    {
        var token = OperationToken;
        if (NetworkBytes + count > NetworkLimit) throw new IOException("目录索引读取量超过 16 MiB，已停止；该压缩包不适合轻量远程预览。");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var request = CreateRequest(_url, offset, offset + count - 1);
                if (_etag is not null) request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(_etag));
                else if (_modified is not null) request.Headers.IfRange = new RangeConditionHeaderValue(_modified.Value);
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (attempt == 0 && IsExpiredLinkResponse(response))
                {
                    _url = await _getUrl(token).ConfigureAwait(false);
                    continue;
                }
                ValidateResponse(response, offset, offset + count - 1, _length);
                if (_etag is not null && response.Headers.ETag?.ToString() != _etag ||
                    _etag is null && _modified is not null && response.Content.Headers.LastModified != _modified)
                    throw new IOException("远端压缩包已经更新，请关闭后重新预览。");
                var bytes = new byte[count];
                using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await body.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                if (bytes.Length == 0) throw new IOException("服务器返回了空分段。");
                NetworkBytes += count;
                token.ThrowIfCancellationRequested();
                _cache.Write(offset, bytes);
                ReadProgress?.Invoke(NetworkBytes, _length);
                return bytes;
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), token).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), token).ConfigureAwait(false);
            }
        }
        throw new IOException("临时下载链接已失效，请重新打开压缩包。");
    }

    /// <summary>识别通用过期状态，以及网盘 DownLoadKey 在密钥过期时返回的 HTTP 200 JSON 错误；不读取响应体。</summary>
    private static bool IsExpiredLinkResponse(HttpResponseMessage response)
    {
        return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Gone ||
            response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentType?.MediaType is "application/json" or "application/problem+json";
    }

    /// <summary>要求服务器按原始字节区间响应，防止内容压缩破坏偏移。</summary>
    private static HttpRequestMessage CreateRequest(string url, long start, long end)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new IOException("网盘没有返回有效的 HTTP 下载链接。");
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(start, end);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        return request;
    }

    /// <summary>验证区间、总长度和响应编码；对返回完整文件的服务器立即关闭响应体。</summary>
    private static long ValidateResponse(HttpResponseMessage response, long start, long end, long? expected)
    {
        if (response.StatusCode == HttpStatusCode.OK)
            throw new IOException("服务器不支持分段读取，或文件版本已改变。已停止预览，不会下载整个压缩包。");
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new IOException($"分段读取失败（HTTP {(int)response.StatusCode}），请刷新后重试。");
        var range = response.Content.Headers.ContentRange;
        if (range is null || range.Unit != "bytes" || range.From != start || range.To != end || range.Length is null ||
            range.Length <= end || expected is not null && range.Length != expected ||
            response.Content.Headers.ContentLength is long size && size != end - start + 1 ||
            response.Content.Headers.ContentEncoding.Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("服务器返回的分段范围不正确，已停止以防止缓存或解压数据损坏。");
        return range.Length.Value;
    }

    /// <summary>清除磁盘与当前内存分段，允许继续按需重新获取。</summary>
    internal void ClearCache()
    {
        _cache.Clear(); _memory = null; _memoryOffset = -1;
    }

    /// <summary>返回全部压缩包分段缓存的实际占用。</summary>
    internal long GetCacheSize()
    {
        return _cache.GetSize();
    }

    /// <summary>流始终支持读取。</summary>
    public override bool CanRead { get { return !_disposed; } }
    /// <summary>流支持随机定位。</summary>
    public override bool CanSeek { get { return !_disposed; } }
    /// <summary>远端文件不可写。</summary>
    public override bool CanWrite { get { return false; } }
    /// <summary>服务器通过 Content-Range 声明的文件总长度。</summary>
    public override long Length { get { return _length; } }
    /// <summary>下一次读取位置，不会触发下载。</summary>
    public override long Position { get { return _position; } set { Seek(value, SeekOrigin.Begin); } }

    /// <summary>读取请求的区间，优先使用内存或磁盘分段；网络等待仅发生在后台归档线程。</summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    /// <summary>按 64 KiB 缓存块逐段读取；支持取消，不预取下一个块。</summary>
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        OperationToken.ThrowIfCancellationRequested();
        var total = 0;
        while (buffer.Length > 0 && _position < _length)
        {
            OperationToken.ThrowIfCancellationRequested();
            var blockSize = Math.Clamp(ActiveBlockSize, BlockSize, 4 * 1024 * 1024);
            var blockOffset = _position / blockSize * blockSize;
            var blockLength = (int)Math.Min(blockSize, _length - blockOffset);
            var hit = true;
            if (_memoryOffset != blockOffset)
            {
                _memory = _cache.Read(blockOffset, blockLength);
                hit = _memory is not null;
                _memory ??= FetchAsync(blockOffset, blockLength).GetAwaiter().GetResult();
                _memoryOffset = blockOffset;
            }
            var within = (int)(_position - blockOffset);
            var count = Math.Min(buffer.Length, blockLength - within);
            _memory!.AsSpan(within, count).CopyTo(buffer);
            if (hit) CacheHitBytes += count;
            buffer = buffer[count..]; total += count; _position += count;
        }
        return total;
    }

    /// <summary>调整读取位置；越过末尾的定位合法，随后读取返回 0。</summary>
    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var next = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(_position + offset), SeekOrigin.End => checked(_length + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        if (next < 0) throw new IOException("压缩包索引包含无效的负偏移。");
        return _position = next;
    }

    /// <summary>只读流无需刷新。</summary>
    public override void Flush() { }
    /// <summary>不支持写入。</summary>
    public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    /// <summary>不支持改变远端文件长度。</summary>
    public override void SetLength(long value) { throw new NotSupportedException(); }
    /// <summary>释放当前内存块，磁盘缓存保留供后续预览使用。</summary>
    protected override void Dispose(bool disposing)
    {
        _disposed = true; _memory = null; base.Dispose(disposing);
    }
}
