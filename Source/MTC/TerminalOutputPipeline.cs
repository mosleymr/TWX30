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
    private static readonly TimeSpan TerminalDisplayDrainDelay = TimeSpan.FromMilliseconds(4);

    private void SetTerminalLivePaused(bool paused)
    {
        if (_gameInstance == null)
        {
            _terminalLivePaused = paused;
            if (CurrentMtcTabContext() is { } tabWithoutGame)
                tabWithoutGame.TerminalLivePaused = _terminalLivePaused;
            if (paused)
                ClearPendingTerminalOutputBacklog();
            else
                ClearPausedTerminalChunks();
            UpdateTerminalLiveSelector();
            return;
        }

        Core.ClientType targetType = paused ? Core.ClientType.Deaf : Core.ClientType.Standard;
        _terminalLivePaused = paused;
        if (CurrentMtcTabContext() is { } tab)
            tab.TerminalLivePaused = _terminalLivePaused;
        if (paused)
            ClearPendingTerminalOutputBacklog();
        else
            ClearPausedTerminalChunks();

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
        => IsEmbeddedTerminalClientDeaf(CurrentMtcTabContext());

    private bool IsEmbeddedTerminalClientDeaf(MtcTabPrototype? owner)
    {
        return (owner?.GameInstance ?? _gameInstance)?.GetClientType(EmbeddedLocalClientIndex) == Core.ClientType.Deaf;
    }

    private bool HasPendingSessionLogBacklog()
        => HasPendingSessionLogBacklog(CurrentMtcTabContext());

    private bool HasPendingSessionLogBacklog(MtcTabPrototype? owner)
    {
        if (!(owner?.PendingSessionLogChunks ?? _pendingSessionLogChunks).IsEmpty)
            return true;

        return owner is not null
            ? Interlocked.CompareExchange(ref owner.SessionLogDrainScheduled, 0, 0) != 0
            : Interlocked.CompareExchange(ref _sessionLogDrainScheduled, 0, 0) != 0;
    }

    private void SyncEmbeddedTerminalClientType(Core.ClientType clientType)
    {
        _terminalLivePaused = clientType == Core.ClientType.Deaf;
        if (CurrentMtcTabContext() is { } tab)
            tab.TerminalLivePaused = _terminalLivePaused;

        if (_terminalLivePaused)
        {
            // Script-driven deafing can happen immediately after an ECHO.  Do
            // not discard already queued display chunks here, or the progress
            // line can disappear before the UI drain paints it.  Manual pause
            // still clears the backlog in SetTerminalLivePaused before it
            // changes the client type.
            ClearPausedTerminalChunks();
            ClearPendingSessionLogChunks();
        }
        else
        {
            ClearPausedTerminalChunks();
            FlushDeferredPanelRefreshes();
        }

        UpdateTerminalLiveSelector();
    }

    private void ClearPendingTerminalOutputBacklog()
    {
        ClearPausedTerminalChunks();
        var owner = CurrentMtcTabContext();
        var displayQueue = owner?.PendingDisplayChunks ?? _pendingDisplayChunks;
        while (displayQueue.TryDequeue(out _))
        {
        }

        ResetTerminalDisplayArtifactFilterState(owner);
        ClearPendingSessionLogChunks(owner);
        if (owner is not null)
            Interlocked.Exchange(ref owner.DisplayDrainScheduled, 0);
        else
            Interlocked.Exchange(ref _displayDrainScheduled, 0);
    }

    private void EnqueueDisplayChunk(byte[] chunk, bool force = false)
        => EnqueueDisplayChunk(CurrentMtcTabContext(), chunk, force);

    private void EnqueueDisplayChunk(MtcTabPrototype? owner, byte[] chunk, bool force = false)
    {
        if (chunk.Length == 0)
            return;
        bool terminalPaused = owner?.TerminalLivePaused ?? _terminalLivePaused;
        if (terminalPaused && !force)
            return;

        (owner?.PendingDisplayChunks ?? _pendingDisplayChunks).Enqueue(new PendingDisplayChunk(chunk));
        ScheduleDisplayDrain(owner, TimeSpan.Zero);
    }

    private void ScheduleDisplayDrain(TimeSpan delay)
        => ScheduleDisplayDrain(CurrentMtcTabContext(), delay);

    private void ScheduleDisplayDrain(MtcTabPrototype? owner, TimeSpan delay)
    {
        if (owner is not null)
        {
            if (Interlocked.Exchange(ref owner.DisplayDrainScheduled, 1) != 0)
                return;
        }
        else if (Interlocked.Exchange(ref _displayDrainScheduled, 1) != 0)
        {
            return;
        }

        if (delay <= TimeSpan.Zero)
        {
            Dispatcher.UIThread.Post(() => DrainPendingDisplayChunks(owner), DispatcherPriority.Input);
            return;
        }

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(delay).ConfigureAwait(false);
                Dispatcher.UIThread.Post(() => DrainPendingDisplayChunks(owner), DispatcherPriority.Input);
            }
            catch
            {
                if (owner is not null)
                    Interlocked.Exchange(ref owner.DisplayDrainScheduled, 0);
                else
                    Interlocked.Exchange(ref _displayDrainScheduled, 0);
            }
        });
    }

    private bool HasPendingTerminalDisplayBacklog()
        => HasPendingTerminalDisplayBacklog(CurrentMtcTabContext());

    private bool HasPendingTerminalDisplayBacklog(MtcTabPrototype? owner)
    {
        if (!(owner?.PendingDisplayChunks ?? _pendingDisplayChunks).IsEmpty)
            return true;

        return owner is not null
            ? Interlocked.CompareExchange(ref owner.DisplayDrainScheduled, 0, 0) != 0
            : Interlocked.CompareExchange(ref _displayDrainScheduled, 0, 0) != 0;
    }

    private void DrainPendingDisplayChunks()
        => DrainPendingDisplayChunks(CurrentMtcTabContext());

    private void DrainPendingDisplayChunks(MtcTabPrototype? owner)
    {
        int processedChunks = 0;
        int processedBytes = 0;
        long startedAt = Stopwatch.GetTimestamp();
        var displayQueue = owner?.PendingDisplayChunks ?? _pendingDisplayChunks;
        var targetBuffer = owner?.Buffer ?? _buffer;
        var targetParser = owner?.Parser ?? _parser;

        const int maxChunksPerPass = 4;
        const int maxBytesPerPass = 16 * 1024;
        const double maxMillisecondsPerPass = 1.25;

        using (targetBuffer.BeginUpdate())
        {
            while (displayQueue.TryDequeue(out PendingDisplayChunk chunk))
            {
                if (chunk.Bytes.Length > 0)
                {
                    targetParser.Feed(chunk.Bytes, chunk.Bytes.Length);
                    processedBytes += chunk.Bytes.Length;
                }

                processedChunks++;

                if (!displayQueue.IsEmpty &&
                    (processedChunks >= maxChunksPerPass ||
                     processedBytes >= maxBytesPerPass ||
                     Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds >= maxMillisecondsPerPass))
                {
                    break;
                }
            }
        }

        if (owner is not null)
            Interlocked.Exchange(ref owner.DisplayDrainScheduled, 0);
        else
            Interlocked.Exchange(ref _displayDrainScheduled, 0);

        if (!displayQueue.IsEmpty)
        {
            ScheduleDisplayDrain(owner, TerminalDisplayDrainDelay);
        }
    }

    private void QueueSessionLogChunk(byte[] chunk)
        => QueueSessionLogChunk(CurrentMtcTabContext(), chunk);

    private void QueueSessionLogChunk(MtcTabPrototype? owner, byte[] chunk)
    {
        if (chunk.Length == 0)
            return;

        (owner?.PendingSessionLogChunks ?? _pendingSessionLogChunks).Enqueue(chunk);
        if (owner is not null)
        {
            if (Interlocked.Exchange(ref owner.SessionLogDrainScheduled, 1) != 0)
                return;
        }
        else if (Interlocked.Exchange(ref _sessionLogDrainScheduled, 1) != 0)
        {
            return;
        }

        _ = System.Threading.Tasks.Task.Run(() => DrainSessionLogChunks(owner));
    }

    private void DrainSessionLogChunks()
        => DrainSessionLogChunks(CurrentMtcTabContext());

    private void DrainSessionLogChunks(MtcTabPrototype? owner)
    {
        var logQueue = owner?.PendingSessionLogChunks ?? _pendingSessionLogChunks;
        var targetLog = owner?.SessionLog ?? _sessionLog;
        try
        {
            while (logQueue.TryDequeue(out byte[]? chunk))
                targetLog.RecordServerData(chunk);
        }
        finally
        {
            if (owner is not null)
            {
                Interlocked.Exchange(ref owner.SessionLogDrainScheduled, 0);
                if (!logQueue.IsEmpty &&
                    Interlocked.Exchange(ref owner.SessionLogDrainScheduled, 1) == 0)
                {
                    _ = System.Threading.Tasks.Task.Run(() => DrainSessionLogChunks(owner));
                }
            }
            else
            {
                Interlocked.Exchange(ref _sessionLogDrainScheduled, 0);
                if (!logQueue.IsEmpty &&
                    Interlocked.Exchange(ref _sessionLogDrainScheduled, 1) == 0)
                {
                    _ = System.Threading.Tasks.Task.Run(DrainSessionLogChunks);
                }
            }
        }
    }

    private void ClearPendingSessionLogChunks()
        => ClearPendingSessionLogChunks(CurrentMtcTabContext());

    private void ClearPendingSessionLogChunks(MtcTabPrototype? owner)
    {
        var logQueue = owner?.PendingSessionLogChunks ?? _pendingSessionLogChunks;
        while (logQueue.TryDequeue(out _))
        {
        }

        if (owner is not null)
            Interlocked.Exchange(ref owner.SessionLogDrainScheduled, 0);
        else
            Interlocked.Exchange(ref _sessionLogDrainScheduled, 0);
    }

    private void ResetTerminalDisplayArtifactFilterState()
        => ResetTerminalDisplayArtifactFilterState(CurrentMtcTabContext());

    private void ResetTerminalDisplayArtifactFilterState(MtcTabPrototype? owner)
    {
        object sync = owner?.TerminalDisplayArtifactSync ?? _terminalDisplayArtifactSync;
        lock (sync)
        {
            if (owner is not null)
            {
                owner.SuppressingPendingNativeMombotEscapeSequence = false;
                owner.SuppressingPendingNativeMombotEscapeCsiBody = false;
                owner.PendingTerminalSyncMarkerLeadByte = false;
                owner.PendingTerminalSyncMarkerUtf8LeadByte = false;
                owner.PendingNativeMombotEscapeEchoSuppressions = 0;
                owner.NativeMombotEscapeEchoSuppressUntilUtcTicks = 0;
                return;
            }

            _suppressingPendingNativeMombotEscapeSequence = false;
            _suppressingPendingNativeMombotEscapeCsiBody = false;
            _pendingTerminalSyncMarkerLeadByte = false;
            _pendingTerminalSyncMarkerUtf8LeadByte = false;
            Interlocked.Exchange(ref _pendingNativeMombotEscapeEchoSuppressions, 0);
            Interlocked.Exchange(ref _nativeMombotEscapeEchoSuppressUntilUtcTicks, 0);
        }
    }

    private void QueuePausedTerminalChunk(byte[] chunk)
        => QueuePausedTerminalChunk(CurrentMtcTabContext(), chunk);

    private void QueuePausedTerminalChunk(MtcTabPrototype? owner, byte[] chunk)
    {
        byte[] filteredChunk = FilterTerminalDisplayArtifacts(owner, chunk);
        if (filteredChunk.Length == 0)
            return;

        var copy = new byte[filteredChunk.Length];
        Buffer.BlockCopy(filteredChunk, 0, copy, 0, filteredChunk.Length);

        if (owner is not null)
        {
            lock (owner.TerminalDisplayArtifactSync)
                owner.PausedTerminalChunks.Add(copy);
            return;
        }

        lock (_pausedTerminalSync)
            _pausedTerminalChunks.Add(copy);
    }

    private void ClearPausedTerminalChunks()
        => ClearPausedTerminalChunks(CurrentMtcTabContext());

    private void ClearPausedTerminalChunks(MtcTabPrototype? owner)
    {
        if (owner is not null)
        {
            lock (owner.TerminalDisplayArtifactSync)
                owner.PausedTerminalChunks.Clear();
            return;
        }

        lock (_pausedTerminalSync)
            _pausedTerminalChunks.Clear();
    }

    private void FlushPausedTerminalChunksToDisplay()
        => FlushPausedTerminalChunksToDisplay(CurrentMtcTabContext());

    private void FlushPausedTerminalChunksToDisplay(MtcTabPrototype? owner)
    {
        bool replayed = false;

        while (true)
        {
            List<byte[]> pending;
            if (owner is not null)
            {
                lock (owner.TerminalDisplayArtifactSync)
                {
                    if (owner.PausedTerminalChunks.Count == 0)
                        break;

                    pending = new List<byte[]>(owner.PausedTerminalChunks);
                    owner.PausedTerminalChunks.Clear();
                }
            }
            else
            {
                lock (_pausedTerminalSync)
                {
                    if (_pausedTerminalChunks.Count == 0)
                        break;

                    pending = new List<byte[]>(_pausedTerminalChunks);
                    _pausedTerminalChunks.Clear();
                }
            }

            var targetBuffer = owner?.Buffer ?? _buffer;
            var targetParser = owner?.Parser ?? _parser;
            using (targetBuffer.BeginUpdate())
            {
                foreach (byte[] chunk in pending)
                {
                    targetParser.Feed(chunk, chunk.Length);
                    replayed = true;
                }
            }
        }

        if (!replayed)
            return;

        if ((owner is null || owner.Id == _activeMtcTabId) && _mombotPromptOpen)
            RedrawMombotPrompt();
        (owner?.Buffer ?? _buffer).Dirty = true;
    }

}
