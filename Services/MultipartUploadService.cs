using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace drive_desktop.Services;

/// <summary>
/// 分片上传配置。
/// </summary>
public sealed class UploadConfiguration
{
    /// <summary>
    /// UploadChunk 接口的完整地址。
    /// 例如：https://127.0.0.1:5001/api/Files/UploadChunk
    /// </summary>
    public string ApiUrl { get; set; } = string.Empty;

    /// <summary>
    /// JWT，可以只填写 Token，也可以填写 "Bearer xxx"。
    /// </summary>
    public string JWT { get; set; } = string.Empty;

    /// <summary>
    /// 单个文件允许同时上传的最大分片数量。
    /// 不同 MultipartUploadService 对象之间不共享此限制。
    /// </summary>
    public int MaxConcurrentChunks { get; set; } = 5;

    /// <summary>
    /// 每个分片的大小。
    /// </summary>
    public long FileChunkSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// HttpClient 超时时间，单位毫秒。
    /// </summary>
    public int HttpClientTimeout { get; set; } = 10_000;

    /// <summary>
    /// 上传文件时使用的缓冲区大小。
    /// </summary>
    public int BufferSize { get; set; } = 128 * 1024;

    /// <summary>
    /// 进度事件最小触发间隔，单位毫秒。
    /// 分片完成、暂停、继续时不受此限制。
    /// </summary>
    public int ProgressReportIntervalMilliseconds { get; set; } = 200;
}

/// <summary>
/// 分片上传服务。
/// 一个对象同一时间只负责上传一个文件。
/// </summary>
public sealed class MultipartUploadService : IDisposable, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly object _progressLock = new();

    private readonly HttpClient _httpClient;
    private readonly Uri _apiUri;
    private readonly AuthenticationHeaderValue? _authorizationHeader;

    // 配置在构造时复制，之后修改外部 UploadConfiguration 不影响当前对象。
    private readonly int _maxConcurrentChunks;
    private readonly long _chunkSize;
    private readonly int _bufferSize;
    private readonly TimeSpan _progressReportInterval;

    private readonly AsyncManualResetEvent _pauseGate = new(initialState: true);

    private CancellationTokenSource? _uploadCancellation;
    private CancellationTokenSource _pauseRequestCancellation = new();
    private TaskCompletionSource<object?>? _uploadFinishedSignal;

    private bool _isRunning;
    private bool _isPaused;
    private bool _disposed;

    // 当前文件的进度状态，只属于当前 MultipartUploadService 对象。
    private readonly Dictionary<int, long> _activeChunkBytes = new();

    private string _progressUploadId = string.Empty;
    private string _progressFilePath = string.Empty;
    private object? _progressUserState;

    private long _totalBytes;
    private long _confirmedUploadedBytes;
    private int _totalChunks;
    private int _completedChunks;

    private long _speedSampleBytes;
    private long _speedSampleTimestamp;
    private double _currentBytesPerSecond;

    private static HashSet<int> NormalizeCompletedChunkIndexes(
        IReadOnlyCollection<int>? completedChunkIndexes,
        int totalChunks)
    {
        var result = new HashSet<int>();

        if (completedChunkIndexes is null)
            return result;

        foreach (int chunkIndex in completedChunkIndexes)
        {
            if (chunkIndex < 0 || chunkIndex >= totalChunks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(completedChunkIndexes),
                    $"已完成分片序号 {chunkIndex} 不在有效范围 " +
                    $"0～{totalChunks - 1} 内。");
            }

            // HashSet 会自动去重。
            result.Add(chunkIndex);
        }

        return result;
    }

    private long CalculateCompletedChunkBytes(
        IReadOnlySet<int> completedChunkIndexes,
        long fileLength)
    {
        long completedBytes = 0;

        foreach (int chunkIndex in completedChunkIndexes)
        {
            long offset =
                checked((long)chunkIndex * _chunkSize);

            long chunkLength =
                Math.Min(
                    _chunkSize,
                    Math.Max(0, fileLength - offset));

            completedBytes =
                checked(completedBytes + chunkLength);
        }

        return Math.Min(completedBytes, fileLength);
    }

    public MultipartUploadService(UploadConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.ApiUrl))
            throw new ArgumentException("ApiUrl 不能为空。", nameof(configuration));

        if (!Uri.TryCreate(
                configuration.ApiUrl,
                UriKind.Absolute,
                out Uri? apiUri))
        {
            throw new ArgumentException(
                "ApiUrl 必须是完整的绝对地址。",
                nameof(configuration));
        }

        if (configuration.MaxConcurrentChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                "MaxConcurrentChunks 必须大于 0。");
        }

        if (configuration.FileChunkSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                "FileChunkSizeBytes 必须大于 0。");
        }

        if (configuration.HttpClientTimeout <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                "HttpClientTimeout 必须大于 0。");
        }

        if (configuration.BufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                "BufferSize 必须大于 0。");
        }

        if (configuration.ProgressReportIntervalMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                "ProgressReportIntervalMilliseconds 必须大于 0。");
        }

        _apiUri = apiUri;
        _maxConcurrentChunks = configuration.MaxConcurrentChunks;
        _chunkSize = configuration.FileChunkSizeBytes;
        _bufferSize = configuration.BufferSize;

        _progressReportInterval =
            TimeSpan.FromMilliseconds(
                configuration.ProgressReportIntervalMilliseconds);

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(
                configuration.HttpClientTimeout)
        };

        string jwt = configuration.JWT?.Trim() ?? string.Empty;

        if (jwt.StartsWith(
                "Bearer ",
                StringComparison.OrdinalIgnoreCase))
        {
            jwt = jwt["Bearer ".Length..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(jwt))
        {
            _authorizationHeader =
                new AuthenticationHeaderValue("Bearer", jwt);
        }
    }

    /// <summary>
    /// 上传进度发生变化。
    /// </summary>
    public event EventHandler<UploadProgressChangedEventArgs>?
        UploadProgressChanged;

    /// <summary>
    /// 上传完成、出错或取消。
    /// 事件参数继承 AsyncCompletedEventArgs，可以直接检查：
    /// e.Cancelled、e.Error。
    /// </summary>
    public event AsyncCompletedEventHandler?
        UploadFileCompleted;

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
                return _isRunning;
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_stateLock)
                return _isPaused;
        }
    }

    /// <summary>
    /// 开始上传文件。
    ///
    /// startChunkIndex 从 0 开始。
    /// 例如传入 5，表示 0～4 号分片已经上传完成，
    /// 初始进度会自动包含这些已跳过的分片。
    ///
    /// 传输错误通过 UploadFileCompleted 的 Error 返回，
    /// 用户取消通过 Cancelled 返回。
    /// </summary>
    public async Task UploadFileTaskAsync(
        string uploadId,
        string localFilePath,
        IReadOnlyCollection<int>? completedChunkIndexes = null,
        CancellationToken cancellationToken = default,
        object? userState = null)
    {
        CancellationTokenSource uploadCancellation;
        TaskCompletionSource<object?> finishedSignal;

        lock (_stateLock)
        {
            ThrowIfDisposed();

            if (_isRunning)
            {
                throw new InvalidOperationException(
                    "当前上传对象已经有一个正在执行的上传任务。" +
                    "如果要同时上传多个文件，请为每个文件创建一个 MultipartUploadService。");
            }

            _pauseRequestCancellation.Dispose();
            _pauseRequestCancellation = new CancellationTokenSource();

            _pauseGate.Set();

            _isRunning = true;
            _isPaused = false;

            _uploadCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            uploadCancellation = _uploadCancellation;

            _uploadFinishedSignal =
                new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            finishedSignal = _uploadFinishedSignal;
        }

        Exception? completedError = null;
        bool completedCancelled = false;

        long fileLength = 0;
        int totalChunks = 0;

        try
        {
            if (string.IsNullOrWhiteSpace(uploadId))
                throw new ArgumentException("uploadId 不能为空。", nameof(uploadId));

            if (string.IsNullOrWhiteSpace(localFilePath))
            {
                throw new ArgumentException(
                    "本地文件地址不能为空。",
                    nameof(localFilePath));
            }

            var fileInfo = new FileInfo(localFilePath);

            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException(
                    "找不到需要上传的本地文件。",
                    localFilePath);
            }

            fileLength = fileInfo.Length;
            totalChunks = CalculateTotalChunks(fileLength, _chunkSize);

            HashSet<int> completedChunkSet =
                NormalizeCompletedChunkIndexes(
                    completedChunkIndexes,
                    totalChunks);

            long confirmedUploadedBytes =
                CalculateCompletedChunkBytes(
                    completedChunkSet,
                    fileLength);

            InitializeProgress(
                uploadId,
                localFilePath,
                userState,
                fileLength,
                totalChunks,
                completedChunkSet.Count,
                confirmedUploadedBytes);

            // 先报告包含断点数据的初始进度。
            RaiseProgress(CreateProgressEventArgs(forceSpeedSample: false));

            await UploadFileCoreAsync(
                    uploadId,
                    localFilePath,
                    fileLength,
                    totalChunks,
                    completedChunkSet,
                    uploadCancellation.Token)
                .ConfigureAwait(false);

            // 确保最后一次进度为 100%。
            RaiseProgress(CreateProgressEventArgs(forceSpeedSample: true));
        }
        catch (OperationCanceledException)
            when (uploadCancellation.IsCancellationRequested)
        {
            completedCancelled = true;
        }
        catch (Exception ex)
        {
            completedError = ex;
        }
        finally
        {
            FinishUploadState(
                uploadCancellation,
                finishedSignal);
        }

        var completedEventArgs = new UploadCompletedEventArgs(
            error: completedError,
            cancelled: completedCancelled,
            userState: userState,
            uploadId: uploadId,
            localFilePath: localFilePath,
            totalBytes: fileLength,
            totalChunks: totalChunks);

        UploadFileCompleted?.Invoke(this, completedEventArgs);
    }

    /// <summary>
    /// UploadFileTaskAsync 的别名。
    /// </summary>
    public Task StartAsync(
        string uploadId,
        string localFilePath,
        IReadOnlyCollection<int>? completedChunkIndexes = null,
        CancellationToken cancellationToken = default,
        object? userState = null)
    {
        return UploadFileTaskAsync(
            uploadId,
            localFilePath,
            completedChunkIndexes,
            cancellationToken,
            userState);
    }

    /// <summary>
    /// 暂停上传。
    ///
    /// 当前正在上传的分片请求会被取消。
    /// Resume 后，这些尚未被服务器确认成功的分片会重新上传。
    /// </summary>
    public void Pause()
    {
        UploadProgressChangedEventArgs? progressEventArgs = null;

        lock (_stateLock)
        {
            ThrowIfDisposed();

            if (!_isRunning || _isPaused)
                return;

            _isPaused = true;

            // 先关闭门，防止新的分片请求开始。
            _pauseGate.Reset();

            // 取消当前文件中正在发送的所有分片请求。
            _pauseRequestCancellation.Cancel();
        }

        progressEventArgs = ResetSpeedAndCreateProgressEventArgs();
        RaiseProgress(progressEventArgs);
    }

    public Task PauseAsync()
    {
        Pause();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 继续上传。
    /// 暂停时未确认成功的分片会重新发送。
    /// </summary>
    public void Resume()
    {
        CancellationTokenSource? oldPauseCancellation = null;
        UploadProgressChangedEventArgs? progressEventArgs = null;

        lock (_stateLock)
        {
            ThrowIfDisposed();

            if (!_isRunning || !_isPaused)
                return;

            oldPauseCancellation = _pauseRequestCancellation;
            _pauseRequestCancellation = new CancellationTokenSource();

            _isPaused = false;
            _pauseGate.Set();
        }

        oldPauseCancellation.Dispose();

        progressEventArgs = ResetSpeedAndCreateProgressEventArgs();
        RaiseProgress(progressEventArgs);
    }

    public Task ResumeAsync()
    {
        Resume();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 取消当前文件上传，并等待 UploadFileCompleted 被触发。
    /// </summary>
    public async Task CancelAsync()
    {
        Task? finishedTask;

        lock (_stateLock)
        {
            if (!_isRunning)
                return;

            _isPaused = false;

            _pauseGate.Set();
            _pauseRequestCancellation.Cancel();
            _uploadCancellation?.Cancel();

            finishedTask = _uploadFinishedSignal?.Task;
        }

        if (finishedTask is not null)
            await finishedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 当前文件内部创建固定数量的上传 Worker。
    ///
    /// 这里的 Worker 数量只属于当前文件，
    /// 不会和其他 MultipartUploadService 共享。
    /// </summary>
    private async Task UploadFileCoreAsync(
        string uploadId,
        string localFilePath,
        long fileLength,
        int totalChunks,
        IReadOnlySet<int> completedChunkIndexes,
        CancellationToken cancellationToken)
    {
        // 根据服务端已经确认的分片，生成真正需要上传的序号。
        var pendingChunkIndexes =
            new int[totalChunks - completedChunkIndexes.Count];

        int pendingPosition = 0;

        for (int chunkIndex = 0;
             chunkIndex < totalChunks;
             chunkIndex++)
        {
            if (completedChunkIndexes.Contains(chunkIndex))
                continue;

            pendingChunkIndexes[pendingPosition] =
                chunkIndex;

            pendingPosition++;
        }

        // 服务端表示全部分片已经完成。
        if (pendingChunkIndexes.Length == 0)
            return;

        int workerCount = Math.Min(
            _maxConcurrentChunks,
            pendingChunkIndexes.Length);

        using var stopOnFailure =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        CancellationToken workerToken =
            stopOnFailure.Token;

        // 这是 pendingChunkIndexes 的位置，不再是分片序号。
        int nextPendingPosition = -1;

        Exception? firstFailure = null;

        async Task WorkerAsync()
        {
            try
            {
                while (true)
                {
                    workerToken.ThrowIfCancellationRequested();

                    int position =
                        Interlocked.Increment(
                            ref nextPendingPosition);

                    if (position >= pendingChunkIndexes.Length)
                        return;

                    // 从待上传列表中取得真正的分片序号。
                    int chunkIndex =
                        pendingChunkIndexes[position];

                    long offset =
                        checked((long)chunkIndex * _chunkSize);

                    long chunkLength =
                        Math.Min(
                            _chunkSize,
                            fileLength - offset);

                    if (fileLength == 0)
                        chunkLength = 0;

                    await UploadChunkWithPauseSupportAsync(
                            uploadId,
                            localFilePath,
                            chunkIndex,
                            offset,
                            chunkLength,
                            workerToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(
                    ref firstFailure,
                    ex,
                    comparand: null);

                stopOnFailure.Cancel();
                throw;
            }
        }

        var workers = new Task[workerCount];

        for (int i = 0; i < workerCount; i++)
            workers[i] = WorkerAsync();

        try
        {
            await Task.WhenAll(workers)
                .ConfigureAwait(false);
        }
        catch
        {
            Exception? capturedFailure =
                Volatile.Read(ref firstFailure);

            if (capturedFailure is not null)
            {
                ExceptionDispatchInfo
                    .Capture(capturedFailure)
                    .Throw();
            }

            throw;
        }
    }

    /// <summary>
    /// 上传单个分片，并处理暂停后的重新发送。
    /// </summary>
    private async Task UploadChunkWithPauseSupportAsync(
        string uploadId,
        string localFilePath,
        int chunkIndex,
        long offset,
        long chunkLength,
        CancellationToken workerToken)
    {
        while (true)
        {
            await _pauseGate
                .WaitAsync(workerToken)
                .ConfigureAwait(false);

            workerToken.ThrowIfCancellationRequested();

            StartChunkAttempt(chunkIndex);

            CancellationTokenSource? requestCancellation = null;
            CancellationToken pauseCancellationToken = default;

            try
            {
                (
                    requestCancellation,
                    pauseCancellationToken
                ) = CreateRequestCancellation(workerToken);

                await UploadSingleChunkAsync(
                        uploadId,
                        localFilePath,
                        chunkIndex,
                        offset,
                        chunkLength,
                        requestCancellation.Token)
                    .ConfigureAwait(false);

                MarkChunkCompleted(
                    chunkIndex,
                    chunkLength);

                return;
            }
            catch (OperationCanceledException)
                when (!workerToken.IsCancellationRequested &&
                      pauseCancellationToken.IsCancellationRequested)
            {
                // 这是暂停导致的请求取消。
                // 清除本次尝试的临时发送字节，Resume 后重新上传。
                AbandonChunkAttempt(chunkIndex);

                await _pauseGate
                    .WaitAsync(workerToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                AbandonChunkAttempt(chunkIndex);
                throw;
            }
            finally
            {
                requestCancellation?.Dispose();
            }
        }
    }

    /// <summary>
    /// 调用后端 UploadChunk 接口。
    /// </summary>
    private async Task<UploadChunkResponse> UploadSingleChunkAsync(
        string uploadId,
        string localFilePath,
        int chunkIndex,
        long offset,
        long chunkLength,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(
            localFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            _bufferSize,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan);

        fileStream.Seek(offset, SeekOrigin.Begin);

        using var fileContent = new ProgressFileSliceContent(
            fileStream,
            chunkLength,
            _bufferSize,
            bytesWritten =>
            {
                AddChunkTransferredBytes(
                    chunkIndex,
                    bytesWritten);
            },
            leaveOpen: true);

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue(
                "application/octet-stream");

        using var multipartContent =
            new MultipartFormDataContent();

        multipartContent.Add(
            new StringContent(
                uploadId,
                Encoding.UTF8),
            "uploadId");

        multipartContent.Add(
            new StringContent(
                chunkIndex.ToString(
                    CultureInfo.InvariantCulture),
                Encoding.UTF8),
            "chunkIndex");

        multipartContent.Add(
            fileContent,
            "file",
            Path.GetFileName(localFilePath));

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                _apiUri)
            {
                Content = multipartContent
            };

        if (_authorizationHeader is not null)
            request.Headers.Authorization = _authorizationHeader;

        using HttpResponseMessage response =
            await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

        string responseText =
            await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

        bool hasDefaultMsg =
            TryParseDefaultMsg(
                responseText,
                out UploadChunkResponse? result);

        // 优先读取服务器 DefaultMsg 中的错误。
        if (hasDefaultMsg && result!.Status != 0)
        {
            throw new UploadApiException(
                result.Status,
                result.Msg,
                result.Data);
        }

        if (!response.IsSuccessStatusCode)
        {
            string body = responseText.Length <= 512
                ? responseText
                : responseText[..512];

            throw new HttpRequestException(
                $"上传分片 {chunkIndex} 失败，" +
                $"HTTP {(int)response.StatusCode} " +
                $"{response.ReasonPhrase}。响应：{body}",
                inner: null,
                response.StatusCode);
        }

        if (!hasDefaultMsg || result is null)
        {
            throw new InvalidDataException(
                $"上传分片 {chunkIndex} 后，" +
                "服务器没有返回有效的 DefaultMsg JSON。");
        }

        return result;
    }

    private (
        CancellationTokenSource RequestCancellation,
        CancellationToken PauseCancellationToken
        ) CreateRequestCancellation(
            CancellationToken workerToken)
    {
        lock (_stateLock)
        {
            CancellationToken pauseToken =
                _pauseRequestCancellation.Token;

            var requestCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    workerToken,
                    pauseToken);

            return (
                requestCancellation,
                pauseToken);
        }
    }

    private void InitializeProgress(
        string uploadId,
        string localFilePath,
        object? userState,
        long totalBytes,
        int totalChunks,
        int completedChunks,
        long confirmedUploadedBytes)
    {
        lock (_progressLock)
        {
            _progressUploadId = uploadId;
            _progressFilePath = localFilePath;
            _progressUserState = userState;

            _totalBytes = totalBytes;
            _totalChunks = totalChunks;

            _completedChunks = completedChunks;
            _confirmedUploadedBytes =
                Math.Min(confirmedUploadedBytes, totalBytes);

            _activeChunkBytes.Clear();

            _speedSampleBytes = 0;
            _currentBytesPerSecond = 0;
            _speedSampleTimestamp =
                Stopwatch.GetTimestamp();
        }
    }

    private void StartChunkAttempt(int chunkIndex)
    {
        lock (_progressLock)
            _activeChunkBytes[chunkIndex] = 0;
    }

    private void AddChunkTransferredBytes(
        int chunkIndex,
        long bytesWritten)
    {
        if (bytesWritten <= 0)
            return;

        UploadProgressChangedEventArgs? eventArgs = null;

        lock (_progressLock)
        {
            if (!_activeChunkBytes.TryGetValue(
                    chunkIndex,
                    out long currentBytes))
            {
                return;
            }

            _activeChunkBytes[chunkIndex] =
                currentBytes + bytesWritten;

            _speedSampleBytes += bytesWritten;

            long now = Stopwatch.GetTimestamp();

            double elapsedSeconds =
                (now - _speedSampleTimestamp) /
                (double)Stopwatch.Frequency;

            if (elapsedSeconds >=
                _progressReportInterval.TotalSeconds)
            {
                UpdateSpeedSampleLocked(
                    now,
                    elapsedSeconds);

                eventArgs =
                    CreateProgressEventArgsLocked();
            }
        }

        if (eventArgs is not null)
            RaiseProgress(eventArgs);
    }

    private void MarkChunkCompleted(
        int chunkIndex,
        long chunkLength)
    {
        UploadProgressChangedEventArgs eventArgs;

        lock (_progressLock)
        {
            _activeChunkBytes.Remove(chunkIndex);

            _confirmedUploadedBytes =
                Math.Min(
                    _totalBytes,
                    _confirmedUploadedBytes +
                    chunkLength);

            _completedChunks =
                Math.Min(
                    _totalChunks,
                    _completedChunks + 1);

            UpdateSpeedForForcedReportLocked();

            eventArgs =
                CreateProgressEventArgsLocked();
        }

        RaiseProgress(eventArgs);
    }

    private void AbandonChunkAttempt(int chunkIndex)
    {
        UploadProgressChangedEventArgs eventArgs;

        lock (_progressLock)
        {
            _activeChunkBytes.Remove(chunkIndex);

            UpdateSpeedForForcedReportLocked();

            eventArgs =
                CreateProgressEventArgsLocked();
        }

        RaiseProgress(eventArgs);
    }

    private UploadProgressChangedEventArgs
        ResetSpeedAndCreateProgressEventArgs()
    {
        lock (_progressLock)
        {
            _speedSampleBytes = 0;
            _currentBytesPerSecond = 0;
            _speedSampleTimestamp =
                Stopwatch.GetTimestamp();

            return CreateProgressEventArgsLocked();
        }
    }

    private UploadProgressChangedEventArgs
        CreateProgressEventArgs(bool forceSpeedSample)
    {
        lock (_progressLock)
        {
            if (forceSpeedSample)
                UpdateSpeedForForcedReportLocked();

            return CreateProgressEventArgsLocked();
        }
    }

    private UploadProgressChangedEventArgs
        CreateProgressEventArgsLocked()
    {
        long activeUploadedBytes = 0;

        foreach (long value in _activeChunkBytes.Values)
            activeUploadedBytes += value;

        long uploadedBytes = Math.Min(
            _totalBytes,
            _confirmedUploadedBytes +
            activeUploadedBytes);

        double percentage;

        if (_totalBytes <= 0)
        {
            percentage =
                _completedChunks >= _totalChunks
                    ? 100
                    : 0;
        }
        else
        {
            percentage =
                uploadedBytes * 100d /
                _totalBytes;
        }

        percentage = Math.Clamp(
            percentage,
            0,
            100);

        long remainingBytes =
            Math.Max(0, _totalBytes - uploadedBytes);

        TimeSpan remainingTime =
            TimeSpan.Zero;

        if (_currentBytesPerSecond > 0 &&
            remainingBytes > 0)
        {
            double remainingSeconds =
                remainingBytes /
                _currentBytesPerSecond;

            if (double.IsFinite(remainingSeconds) &&
                remainingSeconds >= 0 &&
                remainingSeconds <=
                TimeSpan.MaxValue.TotalSeconds)
            {
                remainingTime =
                    TimeSpan.FromSeconds(
                        remainingSeconds);
            }
        }

        return new UploadProgressChangedEventArgs(
            uploadId: _progressUploadId,
            localFilePath: _progressFilePath,
            uploadedBytesSize: uploadedBytes,
            confirmedUploadedBytes:
            _confirmedUploadedBytes,
            totalBytesToUpload: _totalBytes,
            bytesPerSecondSpeed:
            _currentBytesPerSecond,
            progressPercentage: percentage,
            remainingTime: remainingTime,
            completedChunks: _completedChunks,
            totalChunks: _totalChunks,
            activeChunks: _activeChunkBytes.Count,
            userState: _progressUserState);
    }

    private void UpdateSpeedForForcedReportLocked()
    {
        long now = Stopwatch.GetTimestamp();

        double elapsedSeconds =
            (now - _speedSampleTimestamp) /
            (double)Stopwatch.Frequency;

        if (_speedSampleBytes > 0 &&
            elapsedSeconds > 0)
        {
            UpdateSpeedSampleLocked(
                now,
                elapsedSeconds);
        }
    }

    private void UpdateSpeedSampleLocked(
        long now,
        double elapsedSeconds)
    {
        if (elapsedSeconds > 0)
        {
            _currentBytesPerSecond =
                _speedSampleBytes /
                elapsedSeconds;
        }

        _speedSampleBytes = 0;
        _speedSampleTimestamp = now;
    }

    private void RaiseProgress(
        UploadProgressChangedEventArgs eventArgs)
    {
        UploadProgressChanged?.Invoke(
            this,
            eventArgs);
    }

    private void FinishUploadState(
        CancellationTokenSource uploadCancellation,
        TaskCompletionSource<object?> finishedSignal)
    {
        CancellationTokenSource?
            pauseCancellationToDispose = null;

        lock (_stateLock)
        {
            if (ReferenceEquals(
                    _uploadCancellation,
                    uploadCancellation))
            {
                _uploadCancellation = null;
                _uploadFinishedSignal = null;

                _isRunning = false;
                _isPaused = false;

                _pauseGate.Set();
            }

            if (_disposed)
            {
                pauseCancellationToDispose =
                    _pauseRequestCancellation;
            }
        }

        uploadCancellation.Dispose();
        pauseCancellationToDispose?.Dispose();

        // CancelAsync 等待到这里即可返回。
        finishedSignal.TrySetResult(null);
    }

    private static int CalculateTotalChunks(
        long fileLength,
        long chunkSize)
    {
        // 让空文件也产生一个 0 字节分片。
        if (fileLength == 0)
            return 1;

        long totalChunks =
            ((fileLength - 1) / chunkSize) + 1;

        if (totalChunks > int.MaxValue)
        {
            throw new InvalidOperationException(
                "文件分片数量超过 Int32 最大值。");
        }

        return checked((int)totalChunks);
    }

    private static long CalculateSkippedBytes(
        int startChunkIndex,
        long fileLength,
        long chunkSize)
    {
        if (startChunkIndex <= 0 ||
            fileLength <= 0)
        {
            return 0;
        }

        long skippedBytes =
            checked((long)startChunkIndex * chunkSize);

        return Math.Min(
            skippedBytes,
            fileLength);
    }

    private static bool TryParseDefaultMsg(
        string json,
        out UploadChunkResponse? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(json);

            JsonElement root =
                document.RootElement;

            if (root.ValueKind !=
                JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetPropertyIgnoreCase(
                    root,
                    "Status",
                    out JsonElement statusElement))
            {
                return false;
            }

            int status;

            if (statusElement.ValueKind ==
                JsonValueKind.Number &&
                statusElement.TryGetInt32(out int numberStatus))
            {
                status = numberStatus;
            }
            else if (statusElement.ValueKind ==
                     JsonValueKind.String &&
                     int.TryParse(
                         statusElement.GetString(),
                         NumberStyles.Integer,
                         CultureInfo.InvariantCulture,
                         out int stringStatus))
            {
                status = stringStatus;
            }
            else
            {
                return false;
            }

            string msg = string.Empty;

            if (TryGetPropertyIgnoreCase(
                    root,
                    "Msg",
                    out JsonElement msgElement) &&
                msgElement.ValueKind ==
                JsonValueKind.String)
            {
                msg = msgElement.GetString() ??
                      string.Empty;
            }

            JsonElement? data = null;

            if (TryGetPropertyIgnoreCase(
                    root,
                    "Data",
                    out JsonElement dataElement))
            {
                data = dataElement.Clone();
            }

            result = new UploadChunkResponse(
                status,
                msg,
                data);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (JsonProperty property
                 in element.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public void Dispose()
    {
        CancellationTokenSource?
            pauseCancellationToDispose = null;

        lock (_stateLock)
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_isRunning)
            {
                _isPaused = false;

                _pauseGate.Set();
                _pauseRequestCancellation.Cancel();
                _uploadCancellation?.Cancel();
            }
            else
            {
                pauseCancellationToDispose =
                    _pauseRequestCancellation;
            }
        }

        pauseCancellationToDispose?.Dispose();
        _httpClient.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await CancelAsync().ConfigureAwait(false);
        Dispose();
    }
}

/// <summary>
/// 上传进度事件参数。
/// </summary>
public sealed class UploadProgressChangedEventArgs : EventArgs
{
    public UploadProgressChangedEventArgs(
        string uploadId,
        string localFilePath,
        long uploadedBytesSize,
        long confirmedUploadedBytes,
        long totalBytesToUpload,
        double bytesPerSecondSpeed,
        double progressPercentage,
        TimeSpan remainingTime,
        int completedChunks,
        int totalChunks,
        int activeChunks,
        object? userState)
    {
        UploadId = uploadId;
        LocalFilePath = localFilePath;
        UploadedBytesSize = uploadedBytesSize;
        ConfirmedUploadedBytes = confirmedUploadedBytes;
        TotalBytesToUpload = totalBytesToUpload;
        BytesPerSecondSpeed = bytesPerSecondSpeed;
        ProgressPercentage = progressPercentage;
        RemainingTime = remainingTime;
        CompletedChunks = completedChunks;
        TotalChunks = totalChunks;
        ActiveChunks = activeChunks;
        UserState = userState;
    }

    public string UploadId { get; }

    public string LocalFilePath { get; }

    /// <summary>
    /// 已确认分片字节 + 当前正在发送的分片字节。
    /// 已包含 startChunkIndex 跳过的字节。
    /// </summary>
    public long UploadedBytesSize { get; }

    /// <summary>
    /// 已被服务器确认成功的字节。
    /// 已包含 startChunkIndex 跳过的字节。
    /// </summary>
    public long ConfirmedUploadedBytes { get; }

    public long TotalBytesToUpload { get; }

    /// <summary>
    /// 当前文件所有活动分片的聚合上传速度。
    /// </summary>
    public double BytesPerSecondSpeed { get; }

    public double ProgressPercentage { get; }

    public TimeSpan RemainingTime { get; }

    public int CompletedChunks { get; }

    public int TotalChunks { get; }

    public int ActiveChunks { get; }

    public object? UserState { get; }
}

/// <summary>
/// 上传完成事件参数。
/// </summary>
public sealed class UploadCompletedEventArgs :
    AsyncCompletedEventArgs
{
    internal UploadCompletedEventArgs(
        Exception? error,
        bool cancelled,
        object? userState,
        string uploadId,
        string localFilePath,
        long totalBytes,
        int totalChunks)
        : base(error, cancelled, userState)
    {
        UploadId = uploadId;
        LocalFilePath = localFilePath;
        TotalBytes = totalBytes;
        TotalChunks = totalChunks;
    }

    public string UploadId { get; }

    public string LocalFilePath { get; }

    public long TotalBytes { get; }

    public int TotalChunks { get; }
}

/// <summary>
/// UploadChunk 接口返回的数据。
/// Data 使用 JsonElement，避免 NativeAOT 下 object 反序列化问题。
/// </summary>
public sealed class UploadChunkResponse
{
    public UploadChunkResponse(
        int status,
        string msg,
        JsonElement? data)
    {
        Status = status;
        Msg = msg;
        Data = data;
    }

    public int Status { get; }

    public string Msg { get; }

    public JsonElement? Data { get; }
}

/// <summary>
/// 后端返回 Status != 0。
/// </summary>
public sealed class UploadApiException : Exception
{
    public UploadApiException(
        int status,
        string message,
        JsonElement? data)
        : base(
            string.IsNullOrWhiteSpace(message)
                ? $"上传接口返回错误状态：{status}"
                : message)
    {
        Status = status;
        Data = data;
    }

    public int Status { get; }

    public JsonElement? Data { get; }
}

/// <summary>
/// 只读取文件流中指定长度的数据，同时统计实际写入 HTTP 请求流的字节数。
/// 不依赖第三方包。
/// </summary>
public sealed class ProgressFileSliceContent : HttpContent
{
    private readonly Stream _source;
    private readonly long _contentLength;
    private readonly int _bufferSize;
    private readonly Action<long> _progress;
    private readonly bool _leaveOpen;

    public ProgressFileSliceContent(
        Stream source,
        long contentLength,
        int bufferSize,
        Action<long> progress,
        bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(progress);

        if (!source.CanRead)
        {
            throw new ArgumentException(
                "源流必须可读。",
                nameof(source));
        }

        if (contentLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentLength));
        }

        if (bufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bufferSize));
        }

        _source = source;
        _contentLength = contentLength;
        _bufferSize = bufferSize;
        _progress = progress;
        _leaveOpen = leaveOpen;

        Headers.ContentLength = contentLength;
    }

    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        return CopyToTargetAsync(
            stream,
            CancellationToken.None);
    }

    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken)
    {
        return CopyToTargetAsync(
            stream,
            cancellationToken);
    }

    private async Task CopyToTargetAsync(
        Stream target,
        CancellationToken cancellationToken)
    {
        byte[] buffer =
            ArrayPool<byte>.Shared.Rent(_bufferSize);

        try
        {
            long remaining = _contentLength;

            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int requestedCount =
                    (int)Math.Min(
                        buffer.Length,
                        remaining);

                int readCount =
                    await _source.ReadAsync(
                            buffer.AsMemory(
                                0,
                                requestedCount),
                            cancellationToken)
                        .ConfigureAwait(false);

                if (readCount <= 0)
                {
                    throw new EndOfStreamException(
                        $"读取文件分片时提前到达文件末尾，" +
                        $"仍缺少 {remaining} 字节。");
                }

                await target.WriteAsync(
                        buffer.AsMemory(
                            0,
                            readCount),
                        cancellationToken)
                    .ConfigureAwait(false);

                remaining -= readCount;

                // 必须在真正写入请求流之后再统计。
                _progress(readCount);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    protected override bool TryComputeLength(
        out long length)
    {
        length = _contentLength;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
            _source.Dispose();

        base.Dispose(disposing);
    }
}

/// <summary>
/// 支持异步等待的手动重置事件。
/// </summary>
internal sealed class AsyncManualResetEvent
{
    private volatile TaskCompletionSource<bool> _source;

    public AsyncManualResetEvent(bool initialState)
    {
        _source = CreateSource();

        if (initialState)
            _source.TrySetResult(true);
    }

    public Task WaitAsync(
        CancellationToken cancellationToken = default)
    {
        Task task = _source.Task;

        if (!cancellationToken.CanBeCanceled)
            return task;

        return task.WaitAsync(cancellationToken);
    }

    public void Set()
    {
        _source.TrySetResult(true);
    }

    public void Reset()
    {
        while (true)
        {
            TaskCompletionSource<bool> current =
                _source;

            if (!current.Task.IsCompleted)
                return;

            TaskCompletionSource<bool> replacement =
                CreateSource();

            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _source,
                        replacement,
                        current),
                    current))
            {
                return;
            }
        }
    }

    private static TaskCompletionSource<bool>
        CreateSource()
    {
        return new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}