/*
Copyright (C) 2005  Remco Mulder

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation; either version 2 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA

For source notes please refer to Notes.txt
For license terms please refer to GPL.txt.

These files should be stored in the root of the compression you 
received this source in.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TWXProxy.Core
{
    public sealed record BubbleInfo(
        ushort Gate,
        ushort Deepest,
        ushort Size,
        ushort MaxDepth,
        bool Gapped,
        IReadOnlyList<ushort> Sectors);

    /// <summary>
    /// Bubble structure representing a closed area of sectors
    /// </summary>
    internal struct Bubble
    {
        public ushort Gate;
        public ushort Deepest;
        public ushort Size;
        public ushort MaxDepth;
        public bool Gapped;
        public IReadOnlyList<ushort> Sectors;
    }

    /// <summary>
    /// TModBubble: Analyzes the game database to find "bubbles" (closed areas of sectors)
    /// </summary>
    public class ModBubble : TWXModule, IModBubble
    {
        public const int LegacyDefaultMaxBubbleSize = 25;
        public const int DefaultMaxBubbleSize = 150;

        private int _bubbleSize;
        private int _deepestDepth;
        private int _deepestPoint;
        private int _totalBubbles;
        private int _gappedBubbles;
        private int _maxBubbleSize;
        private byte[] _bubblesCovered = Array.Empty<byte>();
        private int[] _areaCovered = Array.Empty<int>();
        private readonly List<ushort> _areaTraversal = new();
        private List<Bubble> _bubbleList = new List<Bubble>();
        private StreamWriter? _targetFile;
        private ITWXDatabase? _analysisDatabase;
        private int _analysisVisitStamp;

        public ModBubble()
        {
            MaxBubbleSize = DefaultMaxBubbleSize;
        }

        #region IModBubble Implementation

        public int MaxBubbleSize
        {
            get => _maxBubbleSize;
            set => _maxBubbleSize = value;
        }

        public bool AllowSectorsSeparatedByGates { get; set; }

        #endregion

        #region Bubble Analysis

        private bool IsClosedArea(SectorData area, ushort areaIndex, ushort last, ushort depth)
        {
            if (_bubbleSize > _maxBubbleSize || !HasUsableWarpList(area))
            {
                return false;
            }

            if (depth > _deepestDepth)
            {
                _deepestPoint = areaIndex;
                _deepestDepth = depth;
            }

            int coveredIndex = areaIndex - 1;
            if (_areaCovered[coveredIndex] != _analysisVisitStamp)
                _areaTraversal.Add((ushort)areaIndex);
            _areaCovered[coveredIndex] = _analysisVisitStamp;

            for (int i = 0; i < 6; i++)
            {
                ushort warp = area.Warp[i];
                
                if (warp == 0)
                    break;
                
                if (warp != last && _areaCovered[warp - 1] != _analysisVisitStamp)
                {
                    var s = _analysisDatabase?.LoadSector(warp) as SectorData;
                    if (s == null)
                        continue;

                    // See if it warps back into here
                    bool warpsBack = false;
                    for (int j = 0; j < 6; j++)
                    {
                        ushort reverseWarp = s.Warp[j];
                        if (reverseWarp == 0)
                            break;

                        if (reverseWarp == areaIndex)
                        {
                            warpsBack = true;
                            break;
                        }
                    }

                    if (warpsBack)
                    {
                        _bubbleSize++;

                        if (!IsClosedArea(s, warp, (ushort)areaIndex, (ushort)(depth + 1)))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool HasUsableWarpList(Sector sector)
        {
            return sector.Explored != ExploreType.No && sector.Warp.Any(warp => warp > 0);
        }

        private int TestBubble(
            ushort gate,
            ushort interior,
            out ushort deepest,
            out bool gapped,
            out ushort maxDepth,
            out IReadOnlyList<ushort> sectors)
        {
            _bubbleSize = 0;
            _deepestDepth = 0;
            deepest = 0;
            gapped = false;
            maxDepth = 0;
            var coveredSectors = new List<ushort>();
            sectors = coveredSectors;

            var database = GlobalModules.Database;
            if (database == null)
                return -1;

            var area = database.LoadSector(interior) as SectorData;
            if (area == null)
                return -1;

            int sectorCount = database.SectorCount;
            if (_areaCovered.Length < sectorCount)
                _areaCovered = new int[sectorCount];
            _areaTraversal.Clear();
            _analysisDatabase = database;
            _analysisVisitStamp++;
            if (_analysisVisitStamp == int.MaxValue)
            {
                Array.Clear(_areaCovered, 0, _areaCovered.Length);
                _analysisVisitStamp = 1;
            }

            if (!IsClosedArea(area, interior, gate, 0))
            {
                return -1;
            }

            // Copy the area covered to a bubble-local sector list.
            foreach (ushort sectorNumber in _areaTraversal)
            {
                coveredSectors.Add(sectorNumber);

                // Check for backdoors
                if (!gapped)
                {
                    var sector = database.LoadSector(sectorNumber);
                    if (sector is SectorData sectorData)
                    {
                        if (HasBackDoors(sectorData))
                            gapped = true;
                    }
                }
            }

            deepest = (ushort)_deepestPoint;
            maxDepth = sectors.Count > 0 ? (ushort)(_deepestDepth + 1) : (ushort)0;
            return _bubbleSize + 1;
        }

        private void MarkBubbleCovered(IEnumerable<ushort> sectors)
        {
            foreach (ushort sectorNumber in sectors)
            {
                if (sectorNumber >= 1 && sectorNumber <= _bubblesCovered.Length)
                    _bubblesCovered[sectorNumber - 1] = 1;
            }
        }

        private static bool IsMergedBubbleGapped(ITWXDatabase database, ushort gate, IReadOnlyCollection<ushort> sectors)
        {
            var mergedSectorSet = new HashSet<ushort>(sectors);

            foreach (ushort sectorNumber in sectors)
            {
                var sector = database.LoadSector(sectorNumber) as SectorData;
                if (sector == null)
                    continue;

                foreach (ushort backdoor in sector.WarpsIn)
                {
                    if (backdoor == gate)
                        continue;

                    if (IsDirectWarp(sector, backdoor))
                        continue;

                    if (!mergedSectorSet.Contains(backdoor))
                        return true;
                }
            }

            return false;
        }

        private static bool HasBackDoors(SectorData sector)
        {
            foreach (ushort warpIn in sector.WarpsIn)
            {
                if (!IsDirectWarp(sector, warpIn))
                    return true;
            }

            return false;
        }

        private static bool IsDirectWarp(Sector sector, ushort candidate)
        {
            for (int i = 0; i < 6; i++)
            {
                ushort warp = sector.Warp[i];
                if (warp == 0)
                    break;

                if (warp == candidate)
                    return true;
            }

            return false;
        }

        #endregion

        #region Public Methods

        public void DumpBubbles()
        {
            var database = GlobalModules.Database;
            if (database == null)
                return;

            string fileName = Path.Combine(
                GlobalModules.ProgramDir,
                $"{database.DatabaseName}_Bubbles.txt"
            );

            try
            {
                using (var writer = new StreamWriter(fileName))
                {
                    ExportBubbles(writer);
                }
            }
            catch
            {
                // Ignore export failures; we still want the on-screen results below.
            }

            WriteBubbles(false);

            // Broadcast results
            string message = $"\r\n{AnsiCodes.ANSI_15}Completed - {_totalBubbles - _gappedBubbles} solid bubbles, " +
                           $"{_gappedBubbles} gapped bubbles (total of {_totalBubbles} bubbles)\r\n" +
                           "Bubbles shown in red are gapped (broken by at least one backdoor)\r\n";
            
            GlobalModules.Server?.Broadcast(message);
        }

        public void ShowBubble(ushort gate, ushort interior)
        {
            var database = GlobalModules.Database;
            if (database == null)
                return;

            _bubbleSize = 0;
            _deepestDepth = 0;

            var area = database.LoadSector(interior) as SectorData;
            if (area == null)
                return;

            if (_areaCovered.Length < database.SectorCount)
                _areaCovered = new int[database.SectorCount];
            _analysisDatabase = database;
            _analysisVisitStamp++;
            if (_analysisVisitStamp == int.MaxValue)
            {
                Array.Clear(_areaCovered, 0, _areaCovered.Length);
                _analysisVisitStamp = 1;
            }
            var gaps = new List<ushort>();

            if (IsClosedArea(area, interior, gate, 0))
            {
                string output = $"{AnsiCodes.ANSI_9}Gate: {AnsiCodes.ANSI_11}{gate}\r\n" +
                              $"{AnsiCodes.ANSI_9}Size: {AnsiCodes.ANSI_11}{_bubbleSize + 1}\r\n" +
                              $"{AnsiCodes.ANSI_9}Deepest Sector: {AnsiCodes.ANSI_11}{_deepestPoint}\r\n" +
                              $"{AnsiCodes.ANSI_9}Interior: {AnsiCodes.ANSI_11}";

                GlobalModules.Server?.Broadcast(output);

                int col = 1;
                for (int i = 1; i <= database.SectorCount; i++)
                {
                    if (_areaCovered[i - 1] == 1)
                    {
                        col++;
                        string sector = i.ToString().PadRight(6);
                        GlobalModules.Server?.Broadcast(sector);

                        if (col >= 8)
                        {
                            GlobalModules.Server?.Broadcast("\r\n          ");
                            col = 1;
                        }

                        var sector2 = database.LoadSector(i);
                        if (sector2 != null)
                        {
                            var backDoors = database.GetBackDoors(sector2, i);
                            gaps.AddRange(backDoors);
                        }
                    }
                }

                if (gaps.Count > 0)
                {
                    string gapOutput = $"\r\n{AnsiCodes.ANSI_9}Back Doors: {AnsiCodes.ANSI_12}";
                    gapOutput += string.Join(" ", gaps);
                    GlobalModules.Server?.Broadcast(gapOutput);
                }
            }
            else
            {
                GlobalModules.Server?.Broadcast($"{AnsiCodes.ANSI_15}No bubble found.");
            }

            GlobalModules.Server?.Broadcast("\r\n\r\n");
        }

        public (int TotalBubbles, int GappedBubbles, int SolidBubbles) GetBubbleCounts()
        {
            var database = GlobalModules.Database;
            if (database == null)
                return (0, 0, 0);

            AnalyzeBubbles(database);

            foreach (var bubble in _bubbleList)
            {
                if (_bubblesCovered[bubble.Gate - 1] != 0)
                    continue;

                _totalBubbles++;
                if (bubble.Gapped)
                    _gappedBubbles++;
            }

            return (_totalBubbles, _gappedBubbles, _totalBubbles - _gappedBubbles);
        }

        public IReadOnlyList<BubbleInfo> GetBubbles()
        {
            var database = GlobalModules.Database;
            if (database == null)
                return Array.Empty<BubbleInfo>();

            AnalyzeBubbles(database);

            return _bubbleList
                .Where(bubble => _bubblesCovered[bubble.Gate - 1] == 0)
                .OrderBy(bubble => bubble.Gate)
                .Select(bubble => new BubbleInfo(
                    bubble.Gate,
                    bubble.Deepest,
                    bubble.Size,
                    bubble.MaxDepth,
                    bubble.Gapped,
                    bubble.Sectors))
                .ToArray();
        }

        public void ExportBubbles(StreamWriter writer)
        {
            _targetFile = writer;
            WriteBubbles(true);
        }

        #endregion

        #region Private Methods

        private void WriteBubbles(bool useFile)
        {
            var database = GlobalModules.Database;
            if (database == null)
                return;

            AnalyzeBubbles(database);

            if (useFile)
            {
                _bubbleList.Sort((a, b) => a.Deepest.CompareTo(b.Deepest));
            }

            // Output bubbles that aren't parts of other bubbles
            foreach (var bubble in _bubbleList)
            {
                if (_bubblesCovered[bubble.Gate - 1] == 0)
                {
                    if (useFile && _targetFile != null)
                    {
                        _targetFile.WriteLine($"{bubble.Deepest} {bubble.Size}");
                    }
                    else
                    {
                        _totalBubbles++;

                        string output;
                        if (bubble.Gapped)
                        {
                            _gappedBubbles++;
                            output = $"{AnsiCodes.ANSI_4}Gate: {AnsiCodes.ANSI_12}{bubble.Gate,-10}" +
                                   $"{AnsiCodes.ANSI_4}Deepest: {AnsiCodes.ANSI_12}{bubble.Deepest,-10}" +
                                   $"{AnsiCodes.ANSI_4}Size: {AnsiCodes.ANSI_12}{bubble.Size}\r\n";
                        }
                        else
                        {
                            output = $"{AnsiCodes.ANSI_3}Gate: {AnsiCodes.ANSI_11}{bubble.Gate,-10}" +
                                   $"{AnsiCodes.ANSI_3}Deepest: {AnsiCodes.ANSI_11}{bubble.Deepest,-10}" +
                                   $"{AnsiCodes.ANSI_3}Size: {AnsiCodes.ANSI_11}{bubble.Size}\r\n";
                        }

                        GlobalModules.Server?.Broadcast(output);
                    }
                }
            }
        }

        private void AnalyzeBubbles(ITWXDatabase database)
        {
            _bubbleList.Clear();
            _totalBubbles = 0;
            _gappedBubbles = 0;
            _bubblesCovered = new byte[database.SectorCount];

            for (int i = 1; i <= database.SectorCount; i++)
            {
                var sector = database.LoadSector(i);
                if (sector == null)
                    continue;

                if (sector.Warp[1] > 0 && _bubblesCovered[i - 1] == 0)
                {
                    if (AllowSectorsSeparatedByGates)
                    {
                        CheckBubbleAllowingGateSeparatedSectors(i, sector);
                    }
                    else
                    {
                        CheckBubble(i, sector.Warp[0]);
                        CheckBubble(i, sector.Warp[1]);

                        if (sector.Warp[2] > 0)
                            CheckBubble(i, sector.Warp[2]);
                        if (sector.Warp[3] > 0)
                            CheckBubble(i, sector.Warp[3]);
                        if (sector.Warp[4] > 0)
                            CheckBubble(i, sector.Warp[4]);
                        if (sector.Warp[5] > 0)
                            CheckBubble(i, sector.Warp[5]);
                    }
                }
            }
        }

        private void CheckBubble(int gate, ushort interior)
        {
            int size = TestBubble(
                (ushort)gate,
                interior,
                out ushort deepest,
                out bool gapped,
                out ushort maxDepth,
                out IReadOnlyList<ushort> sectors);

            if (size > 1)
            {
                var bubble = new Bubble
                {
                    Gate = (ushort)gate,
                    Deepest = deepest,
                    Size = (ushort)size,
                    MaxDepth = maxDepth,
                    Gapped = gapped,
                    Sectors = sectors,
                };

                _bubbleList.Add(bubble);
                MarkBubbleCovered(bubble.Sectors);
            }
        }

        private void CheckBubbleAllowingGateSeparatedSectors(int gate, Sector gateSector)
        {
            var database = GlobalModules.Database;
            if (database == null)
                return;

            var mergedSectorSet = new HashSet<ushort>();
            var mergedSectors = new List<ushort>();
            ushort deepest = 0;
            ushort maxDepth = 0;

            foreach (ushort interior in gateSector.Warp.Where(warp => warp > 0).Distinct())
            {
                int size = TestBubble(
                    (ushort)gate,
                    interior,
                    out ushort branchDeepest,
                    out _,
                    out ushort branchDepth,
                    out IReadOnlyList<ushort> branchSectors);

                if (size <= 0)
                    continue;

                foreach (ushort sectorNumber in branchSectors)
                {
                    if (mergedSectorSet.Add(sectorNumber))
                        mergedSectors.Add(sectorNumber);
                }

                if (branchDepth > maxDepth)
                {
                    maxDepth = branchDepth;
                    deepest = branchDeepest;
                }
            }

            if (mergedSectors.Count > 1)
            {
                ushort gateCountedDepth = maxDepth > 0 ? (ushort)(maxDepth + 1) : (ushort)0;
                bool gapped = IsMergedBubbleGapped(database, (ushort)gate, mergedSectors);

                var bubble = new Bubble
                {
                    Gate = (ushort)gate,
                    Deepest = deepest,
                    Size = (ushort)mergedSectors.Count,
                    MaxDepth = gateCountedDepth,
                    Gapped = gapped,
                    Sectors = mergedSectors,
                };

                _bubbleList.Add(bubble);
                MarkBubbleCovered(bubble.Sectors);
            }
        }

        #endregion
    }
}
