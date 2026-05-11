using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using SkiaSharp;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Core = TWXProxy.Core;

namespace MTC;

public partial class MainWindow
{
    private void SetTerminalLivePaused(bool paused)
    {
        if (_gameInstance == null)
        {
            _terminalLivePaused = paused;
            if (paused)
                ClearPendingTerminalOutputBacklog();
            else
                ClearPausedTerminalChunks();
            UpdateTerminalLiveSelector();
            return;
        }

        Core.ClientType targetType = paused ? Core.ClientType.Deaf : Core.ClientType.Standard;
        if (_gameInstance.GetClientType(EmbeddedLocalClientIndex) == targetType)
        {
            SyncEmbeddedTerminalClientType(targetType);
            return;
        }

        _gameInstance.SetClientType(EmbeddedLocalClientIndex, targetType);
    }

    private void ApplyEmbeddedTerminalOutputMode()
    {
        if (_gameInstance == null)
            return;

        _gameInstance.SetClientType(
            EmbeddedLocalClientIndex,
            _terminalLivePaused ? Core.ClientType.Deaf : Core.ClientType.Standard);
    }

    private bool IsEmbeddedTerminalClientDeaf()
    {
        return _gameInstance?.GetClientType(EmbeddedLocalClientIndex) == Core.ClientType.Deaf;
    }

    private void SyncEmbeddedTerminalClientType(Core.ClientType clientType)
    {
        _terminalLivePaused = clientType == Core.ClientType.Deaf;

        if (_terminalLivePaused)
            ClearPendingTerminalOutputBacklog();
        else
            ClearPausedTerminalChunks();

        UpdateTerminalLiveSelector();
    }

    private void ClearPendingTerminalOutputBacklog()
    {
        ClearPausedTerminalChunks();
        while (_pendingDisplayChunks.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _displayDrainScheduled, 0);
    }

    private void EnqueueDisplayChunk(byte[] chunk, int lineCount)
    {
        if (chunk.Length == 0)
            return;

        _pendingDisplayChunks.Enqueue(new PendingDisplayChunk(chunk, lineCount));
        if (Interlocked.Exchange(ref _displayDrainScheduled, 1) != 0)
            return;

        Dispatcher.UIThread.Post(DrainPendingDisplayChunks, DispatcherPriority.Render);
    }

    private bool HasPendingTerminalDisplayBacklog()
    {
        if (!_pendingDisplayChunks.IsEmpty)
            return true;

        return Interlocked.CompareExchange(ref _displayDrainScheduled, 0, 0) != 0;
    }

    private void DrainPendingDisplayChunks()
    {
        bool replayed = false;
        int processedChunks = 0;
        int processedBytes = 0;
        long startedAt = Stopwatch.GetTimestamp();

        const int maxChunksPerPass = 8;
        const int maxBytesPerPass = 16 * 1024;
        const double maxMillisecondsPerPass = 4.0;

        using (_buffer.BeginUpdate())
        {
            while (_pendingDisplayChunks.TryDequeue(out PendingDisplayChunk chunk))
            {
                if (chunk.Bytes.Length > 0)
                {
                    _parser.Feed(chunk.Bytes, chunk.Bytes.Length);
                    replayed = true;
                    processedBytes += chunk.Bytes.Length;
                }

                processedChunks++;

                if (!_pendingDisplayChunks.IsEmpty &&
                    (processedChunks >= maxChunksPerPass ||
                     processedBytes >= maxBytesPerPass ||
                     Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds >= maxMillisecondsPerPass))
                {
                    break;
                }
            }
        }

        Interlocked.Exchange(ref _displayDrainScheduled, 0);

        if (!_pendingDisplayChunks.IsEmpty &&
            Interlocked.Exchange(ref _displayDrainScheduled, 1) == 0)
        {
            Dispatcher.UIThread.Post(DrainPendingDisplayChunks, DispatcherPriority.Render);
        }

        if (replayed)
            _buffer.Dirty = true;
    }

    private void QueuePausedTerminalChunk(byte[] chunk)
    {
        byte[] filteredChunk = FilterTerminalDisplayArtifacts(chunk);
        if (filteredChunk.Length == 0)
            return;

        var copy = new byte[filteredChunk.Length];
        Buffer.BlockCopy(filteredChunk, 0, copy, 0, filteredChunk.Length);

        lock (_pausedTerminalSync)
            _pausedTerminalChunks.Add(copy);
    }

    private void ClearPausedTerminalChunks()
    {
        lock (_pausedTerminalSync)
            _pausedTerminalChunks.Clear();
    }

    private void FlushPausedTerminalChunksToDisplay()
    {
        bool replayed = false;

        while (true)
        {
            List<byte[]> pending;
            lock (_pausedTerminalSync)
            {
                if (_pausedTerminalChunks.Count == 0)
                    break;

                pending = new List<byte[]>(_pausedTerminalChunks);
                _pausedTerminalChunks.Clear();
            }

            using (_buffer.BeginUpdate())
            {
                foreach (byte[] chunk in pending)
                {
                    _parser.Feed(chunk, chunk.Length);
                    replayed = true;
                }
            }
        }

        if (!replayed)
            return;

        if (_mombotPromptOpen)
            RedrawMombotPrompt();
        _buffer.Dirty = true;
    }

}
