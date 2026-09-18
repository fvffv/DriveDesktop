using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using Drive.Plugin.Abi;
using Drive.Plugin.SDK;

namespace drive_desktop.Services.Plugins;

/// <summary>分发只包含 SDK 快照的宿主事件，并合并一次操作中的批量删除结果。</summary>
public static class PluginEventHub
{
    private static readonly AsyncLocal<DeletionBatch?> CurrentBatch = new();
    /// <summary>宿主观察到业务事件时通知插件服务。</summary>
    public static event Action<DriveEvent>? Published;
    /// <summary>发布业务事件；批量删除作用域内先记录成功条目，结束后一次通知。</summary>
    public static void Publish(DriveEvent data)
    {
        if (data.Id == DriveEventId.FilesDeleted && CurrentBatch.Value is { } batch && batch.Add(data)) return;
        try { Published?.Invoke(data); }
        catch (Exception error) { Debug.WriteLine(error); }
    }

    /// <summary>开始一次批量删除；使用 using 释放，异常退出时仍通知已经成功删除的部分。</summary>
    /// <returns>仅影响当前异步调用链的作用域；嵌套作用域的成功结果合并到外层。</returns>
    public static IDisposable BeginDeletionBatch()
    {
        var batch = new DeletionBatch(CurrentBatch.Value);
        CurrentBatch.Value = batch;
        return batch;
    }

    private sealed class DeletionBatch(DeletionBatch? parent) : IDisposable
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _files = new(StringComparer.Ordinal);
        private readonly HashSet<string> _folders = new(StringComparer.Ordinal);
        private bool _disposed;

        /// <summary>记录服务端已确认成功的条目；关闭后的作用域不再收集。</summary>
        public bool Add(DriveEvent data)
        {
            lock (_gate)
            {
                if (_disposed) return false;
                foreach (var id in data.FileIds) if (!string.IsNullOrEmpty(id)) _files.Add(id);
                foreach (var id in data.FolderIds) if (!string.IsNullOrEmpty(id)) _folders.Add(id);
                return true;
            }
        }

        /// <summary>恢复外层作用域，并发布去重后的成功删除 ID。</summary>
        public void Dispose()
        {
            DriveEvent data;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                data = new() { Id = DriveEventId.FilesDeleted, FileIds = [.. _files], FolderIds = [.. _folders] };
            }
            CurrentBatch.Value = parent;
            if (data.FileIds.Length != 0 || data.FolderIds.Length != 0) Publish(data);
        }
    }
}
