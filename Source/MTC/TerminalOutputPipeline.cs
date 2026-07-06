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
    private static readonly TimeSpan TerminalDisplayDrainDelay = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan TerminalCatchUpDisplayDrainDelay = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan InactiveDisplayDrainYieldDelay = TimeSpan.FromMilliseconds(10);
    private const int ActiveDisplayDrainChunkLimit = 16;
    private const int ActiveDisplayDrainByteLimit = 256 * 1024;
    private const double ActiveDisplayDrainMilliseconds = 3.5;
    private const int CatchUpDisplayDrainThreshold = 8;
    private const int CatchUpDisplayDrainChunkLimit = 64;
    private const int CatchUpDisplayDrainByteLimit = 1024 * 1024;
    private const double CatchUpDisplayDrainMilliseconds = 8;
    private const int InactiveDisplayDrainChunkLimit = 256;
    private const int InactiveDisplayDrainByteLimit = 512 * 1024;
    private const double InactiveDisplayDrainMilliseconds = 8;

    private MtcTabPrototype? ResolveTerminalOwner(MtcTabPrototype? owner)
    {
        if (owner is not null)
            return owner;
        return ResolveCurrentMtcTabContext();
    }

    private void SetTerminalLivePaused(bool paused)
    {
        var owner = ResolveCurrentMtcTabContext();
        if (_gameInstance == null)
        {
            _terminalLivePaused = paused;
            if (owner is not null)
                owner.TerminalLivePaused = _terminalLivePaused;
            if (paused)
                ClearPendingTerminalOutputBacklog(owner);
            else
                ClearPausedTerminalChunks(owner);
            UpdateTerminalLiveSelector();
            return;
        }

        Core.ClientType targetType = paused ? Core.ClientType.Deaf : Core.ClientType.Standard;
        _terminalLivePaused = paused;
        if (owner is not null)
            owner.TerminalLivePaused = _terminalLivePaused;
        if (paused)
            ClearPendingTerminalOutputBacklog(owner);
        else
            ClearPausedTerminalChunks(owner);

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
        => IsEmbeddedTerminalClientDeaf(ResolveCurrentMtcTabContext());

    private bool IsEmbeddedTerminalClientDeaf(MtcTabPrototype? owner)
    {
        return (owner?.GameInstance ?? _gameInstance)?.GetClientType(EmbeddedLocalClientIndex) == Core.ClientType.Deaf;
    }

    private bool HasPendingSessionLogBacklog()
        => HasPendingSessionLogBacklog(ResolveCurrentMtcTabContext());

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
        var owner = ResolveCurrentMtcTabContext();
        _terminalLivePaused = clientType == Core.ClientType.Deaf;
        if (owner is not null)
            owner.TerminalLivePaused = _terminalLivePaused;

        if (_terminalLivePaused)
        {
            // Script-driven deafing can happen immediately after an ECHO.  Do
            // not discard already queued display chunks here, or the progress
            // line can disappear before the UI drain paints it.  Manual pause
            // still clears the backlog in SetTerminalLivePaused before it
            // changes the client type.
            ClearPausedTerminalChunks(owner);
            ClearPendingSessionLogChunks(owner);
        }
        else
        {
            ClearPausedTerminalChunks(owner);
            FlushDeferredPanelRefreshes();
        }

        UpdateTerminalLiveSelector();
    }

    private void ClearPendingTerminalOutputBacklog()
        => ClearPendingTerminalOutputBacklog(ResolveCurrentMtcTabContext());

    private void ClearPendingTerminalOutputBacklog(MtcTabPrototype? owner)
    {
        ClearPausedTerminalChunks(owner);
        var displayQueue = owner?.PendingDisplayChunks ?? _pendingDisplayChunks;
        while (displayQueue.TryDequeue(out _))
        {
        }

        if (owner is not null)
            Interlocked.Exchange(ref owner.PendingDisplayChunkCount, 0);

        ResetTerminalDisplayArtifactFilterState(owner);
        ClearPendingSessionLogChunks(owner);
        if (owner is not null)
            Interlocked.Exchange(ref owner.DisplayDrainScheduled, 0);
        else
            Interlocked.Exchange(ref _displayDrainScheduled, 0);
    }

    private void EnqueueDisplayChunk(byte[] chunk, bool force = false)
        => EnqueueDisplayChunk(ResolveCurrentMtcTabContext(), chunk, force);

    private void EnqueueDisplayChunk(MtcTabPrototype? owner, byte[] chunk, bool force = false)
    {
        owner = ResolveTerminalOwner(owner);
        if (owner is null)
            return;

        if (chunk.Length == 0)
            return;
        bool terminalPaused = owner?.TerminalLivePaused ?? _terminalLivePaused;
        if (terminalPaused && !force)
            return;

        (owner?.PendingDisplayChunks ?? _pendingDisplayChunks).Enqueue(new PendingDisplayChunk(chunk));
        if (owner is not null)
            Interlocked.Increment(ref owner.PendingDisplayChunkCount);

        if (owner is not null && owner.Id != _activeMtcTabId)
        {
            ScheduleInactiveDisplayDrain(owner);
            return;
        }

        ScheduleDisplayDrain(owner, TimeSpan.Zero);
    }

    private void ScheduleInactiveDisplayDrain(MtcTabPrototype owner)
    {
        if (owner.Id == Volatile.Read(ref _activeMtcTabId))
        {
            ScheduleDisplayDrain(owner, TimeSpan.Zero);
            return;
        }

        if (Interlocked.Exchange(ref owner.DisplayDrainScheduled, 1) != 0)
            return;

        _ = System.Threading.Tasks.Task.Run(() => DrainInactiveDisplayChunks(owner));
    }

    private void DrainInactiveDisplayChunks(MtcTabPrototype owner)
    {
        try
        {
            ExecuteInMtcTabBackgroundContext(owner, () =>
            {
                while (owner.Id != Volatile.Read(ref _activeMtcTabId) &&
                       !owner.PendingDisplayChunks.IsEmpty)
                {
                    int processed = DrainDisplayQueueToBuffer(
                        owner,
                        InactiveDisplayDrainChunkLimit,
                        InactiveDisplayDrainByteLimit,
                        InactiveDisplayDrainMilliseconds,
                        lockBuffer: true);
                    if (processed == 0)
                        break;
                }
            });
        }
        finally
        {
            Interlocked.Exchange(ref owner.DisplayDrainScheduled, 0);

            if (owner.PendingDisplayChunks.IsEmpty)
            {
                // Nothing left to hand off.
            }
            else if (owner.Id == Volatile.Read(ref _activeMtcTabId))
            {
                ScheduleDisplayDrain(owner, TimeSpan.Zero);
            }
            else
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(InactiveDisplayDrainYieldDelay).ConfigureAwait(false);
                    ScheduleInactiveDisplayDrain(owner);
                });
            }
        }
    }

    private void ScheduleDisplayDrain(TimeSpan delay)
        => ScheduleDisplayDrain(ResolveCurrentMtcTabContext(), delay);

    private void ScheduleDisplayDrain(MtcTabPrototype? owner, TimeSpan delay)
    {
        owner = ResolveTerminalOwner(owner);
        if (owner is null)
            return;

        if (owner.Id != _activeMtcTabId)
        {
            ScheduleInactiveDisplayDrain(owner);
            return;
        }

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
            Dispatcher.UIThread.Post(() => DrainPendingDisplayChunks(owner), DispatcherPriority.Background);
            return;
        }

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(delay).ConfigureAwait(false);
                Dispatcher.UIThread.Post(() => DrainPendingDisplayChunks(owner), DispatcherPriority.Background);
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
        => HasPendingTerminalDisplayBacklog(ResolveCurrentMtcTabContext());

    private bool HasPendingTerminalDisplayBacklog(MtcTabPrototype? owner)
    {
        if (!(owner?.PendingDisplayChunks ?? _pendingDisplayChunks).IsEmpty)
            return true;

        return owner is not null
            ? Interlocked.CompareExchange(ref owner.DisplayDrainScheduled, 0, 0) != 0
            : Interlocked.CompareExchange(ref _displayDrainScheduled, 0, 0) != 0;
    }

    private void DrainPendingDisplayChunks()
        => DrainPendingDisplayChunks(ResolveCurrentMtcTabContext());

    private void DrainPendingDisplayChunks(MtcTabPrototype? owner)
    {
        owner = ResolveTerminalOwner(owner);
        if (owner is null)
            return;

        var ownerTab = owner;

        if (ownerTab.Id != _activeMtcTabId)
        {
            ScheduleInactiveDisplayDrain(ownerTab);
            return;
        }

        if (!IsMtcTabBackgroundContext(ownerTab))
        {
            ExecuteInMtcTabBackgroundContext(ownerTab, () => DrainPendingDisplayChunks(ownerTab));
            return;
        }

        int queuedAtStart = Volatile.Read(ref ownerTab.PendingDisplayChunkCount);
        bool catchUp = queuedAtStart >= CatchUpDisplayDrainThreshold;

        _ = DrainDisplayQueueToBuffer(
            ownerTab,
            catchUp ? CatchUpDisplayDrainChunkLimit : ActiveDisplayDrainChunkLimit,
            catchUp ? CatchUpDisplayDrainByteLimit : ActiveDisplayDrainByteLimit,
            catchUp ? CatchUpDisplayDrainMilliseconds : ActiveDisplayDrainMilliseconds,
            lockBuffer: false);

        Interlocked.Exchange(ref ownerTab.DisplayDrainScheduled, 0);

        if (!ownerTab.PendingDisplayChunks.IsEmpty)
        {
            var nextDelay = Volatile.Read(ref ownerTab.PendingDisplayChunkCount) >= CatchUpDisplayDrainThreshold
                ? TerminalCatchUpDisplayDrainDelay
                : TerminalDisplayDrainDelay;
            ScheduleDisplayDrain(ownerTab, nextDelay);
        }
    }

    private void FlushPendingDisplayChunksToBuffer(MtcTabPrototype owner)
    {
        if (owner.PendingDisplayChunks.IsEmpty)
        {
            Interlocked.Exchange(ref owner.DisplayDrainScheduled, 0);
            return;
        }

        ExecuteInMtcTabBackgroundContext(owner, () =>
        {
            lock (owner.TerminalBufferSync)
            {
                while (!owner.PendingDisplayChunks.IsEmpty)
                {
                    int processed = DrainDisplayQueueToBuffer(
                        owner,
                        int.MaxValue,
                        int.MaxValue,
                        maxMillisecondsPerPass: 0,
                        lockBuffer: false);
                    if (processed == 0)
                        break;
                }
            }
        });

        Interlocked.Exchange(ref owner.DisplayDrainScheduled, 0);
    }

    private int DrainDisplayQueueToBuffer(
        MtcTabPrototype ownerTab,
        int maxChunksPerPass,
        int maxBytesPerPass,
        double maxMillisecondsPerPass,
        bool lockBuffer)
    {
        int processedChunks = 0;
        int processedBytes = 0;
        long startedAt = Stopwatch.GetTimestamp();
        var displayQueue = ownerTab.PendingDisplayChunks;
        var targetBuffer = ownerTab.Buffer;
        var targetParser = ownerTab.Parser;

        void Drain()
        {
            using (targetBuffer.BeginUpdate())
            {
                while (displayQueue.TryDequeue(out PendingDisplayChunk chunk))
                {
                    DecrementPendingDisplayChunkCount(ownerTab);

                    if (chunk.Bytes.Length > 0)
                    {
                        targetParser.Feed(chunk.Bytes, chunk.Bytes.Length);
                        processedBytes += chunk.Bytes.Length;
                    }

                    processedChunks++;

                    if (!displayQueue.IsEmpty &&
                        (processedChunks >= maxChunksPerPass ||
                         processedBytes >= maxBytesPerPass ||
                         (maxMillisecondsPerPass > 0 &&
                          Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds >= maxMillisecondsPerPass)))
                    {
                        break;
                    }
                }
            }
        }

        if (lockBuffer)
        {
            lock (ownerTab.TerminalBufferSync)
                Drain();
        }
        else
        {
            Drain();
        }

        return processedChunks;
    }

    private static void DecrementPendingDisplayChunkCount(MtcTabPrototype ownerTab)
    {
        while (true)
        {
            int current = Volatile.Read(ref ownerTab.PendingDisplayChunkCount);
            if (current <= 0)
                return;

            if (Interlocked.CompareExchange(ref ownerTab.PendingDisplayChunkCount, current - 1, current) == current)
                return;
        }
    }

    private void QueueSessionLogChunk(byte[] chunk)
        => QueueSessionLogChunk(ResolveCurrentMtcTabContext(), chunk);

    private void QueueSessionLogChunk(MtcTabPrototype? owner, byte[] chunk)
    {
        owner = ResolveTerminalOwner(owner);
        if (owner is null)
            return;

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
        => DrainSessionLogChunks(ResolveCurrentMtcTabContext());

    private void DrainSessionLogChunks(MtcTabPrototype? owner)
    {
        owner = ResolveTerminalOwner(owner);
        if (owner is null)
            return;

        if (!IsMtcTabBackgroundContext(owner))
        {
            ExecuteInMtcTabBackgroundContext(owner, () => DrainSessionLogChunks(owner));
            return;
        }

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
                    _ = System.Threading.Tasks.Task.Run(() => DrainSessionLogChunks(owner));
                }
            }
        }
    }

    private void ClearPendingSessionLogChunks()
        => ClearPendingSessionLogChunks(ResolveCurrentMtcTabContext());

    private void ClearPendingSessionLogChunks(MtcTabPrototype? owner)
    {
        owner = ResolveTerminalOwner(owner);
        if (owner is null)
            return;

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
        => ResetTerminalDisplayArtifactFilterState(ResolveCurrentMtcTabContext());

    private void ResetTerminalDisplayArtifactFilterState(MtcTabPrototype? owner)
    {
        owner = ResolveTerminalOwner(owner);
        if (owner is null)
            return;

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
        => QueuePausedTerminalChunk(ResolveCurrentMtcTabContext(), chunk);

    private void QueuePausedTerminalChunk(MtcTabPrototype? owner, byte[] chunk)
    {
        owner = ResolveTerminalOwner(owner);
        if (owner is null)
            return;

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
        => ClearPausedTerminalChunks(ResolveCurrentMtcTabContext());

    private void ClearPausedTerminalChunks(MtcTabPrototype? owner)
    {
        owner = ResolveTerminalOwner(owner);
        if (owner is null)
            return;

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
        => FlushPausedTerminalChunksToDisplay(ResolveCurrentMtcTabContext());

    private void FlushPausedTerminalChunksToDisplay(MtcTabPrototype? owner)
    {
        owner = ResolveTerminalOwner(owner);
        if (owner is null)
            return;

        if (!IsMtcTabBackgroundContext(owner))
        {
            ExecuteInMtcTabBackgroundContext(owner, () => FlushPausedTerminalChunksToDisplay(owner));
            return;
        }

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

        bool promptOpen = owner?.MombotPromptOpen ?? _mombotPromptOpen;
        if ((owner is null || owner.Id == _activeMtcTabId) && promptOpen)
            ExecuteInOptionalMtcTabSession(owner, RedrawMombotPrompt);
        (owner?.Buffer ?? _buffer).Dirty = true;
    }

}
