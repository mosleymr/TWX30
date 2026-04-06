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
    /// <summary>
    /// Bubble structure representing a closed area of sectors
    /// </summary>
    internal struct Bubble
    {
        public ushort Gate;
        public ushort Deepest;
        public ushort Size;
        public bool Gapped;
    }

    /// <summary>
    /// TModBubble: Analyzes the game database to find "bubbles" (closed areas of sectors)
    /// </summary>
    public class ModBubble : TWXModule, IModBubble
    {
        private int _bubbleSize;
        private int _deepestDepth;
        private int _deepestPoint;
        private int _totalBubbles;
        private int _gappedBubbles;
        private int _maxBubbleSize;
        private byte[] _bubblesCovered = Array.Empty<byte>();
        private byte[] _areaCovered = Array.Empty<byte>();
        private List<Bubble> _bubbleList = new List<Bubble>();
        private StreamWriter? _targetFile;

        public ModBubble()
        {
            MaxBubbleSize = 25;
        }

        #region IModBubble Implementation

        public int MaxBubbleSize
        {
            get => _maxBubbleSize;
            set => _maxBubbleSize = value;
        }

        #endregion

        #region Bubble Analysis

        private bool IsClosedArea(Sector area, int areaIndex, ushort last, ushort depth)
        {
            if (_bubbleSize > _maxBubbleSize || area.Explored == ExploreType.No)
            {
                return false;
            }

            if (depth > _deepestDepth)
            {
                _deepestPoint = areaIndex;
                _deepestDepth = depth;
            }

            _areaCovered[areaIndex - 1] = 1;

            for (int i = 0; i < 6; i++)
            {
                ushort warp = area.Warp[i];
                
                if (warp == 0)
                    break;
                
                if (warp != last && _areaCovered[warp - 1] == 0 && area.Explored != ExploreType.No)
                {
                    var s = GlobalModules.Database?.LoadSector(warp);
                    if (s == null)
                        continue;

                    // See if it warps back into here
                    bool warpsBack = s.Warp.Any(w => w == areaIndex);

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

        private int TestBubble(ushort gate, ushort interior, out ushort deepest, out bool gapped)
        {
            _bubbleSize = 0;
            _deepestDepth = 0;
            deepest = 0;
            gapped = false;

            var database = GlobalModules.Database;
            if (database == null)
                return -1;

            var area = database.LoadSector(interior);
            if (area == null)
                return -1;

            int sectorCount = database.SectorCount;
            _areaCovered = new byte[sectorCount];

            if (!IsClosedArea(area, interior, gate, 0))
            {
                return -1;
            }

            // Copy the area covered to bubbles covered
            for (int i = 0; i < sectorCount; i++)
            {
                if (_areaCovered[i] == 1)
                {
                    // Check for backdoors
                    if (!gapped)
                    {
                        int areaIndex = i + 1;
                        var sector = database.LoadSector(areaIndex);
                        if (sector != null)
                        {
                            var backDoors = database.GetBackDoors(sector, areaIndex);
                            if (backDoors.Count > 0)
                                gapped = true;
                        }
                    }

                    _bubblesCovered[i] = 1;
                }
            }

            deepest = (ushort)_deepestPoint;
            return _bubbleSize + 1;
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

            var area = database.LoadSector(interior);
            if (area == null)
                return;

            _areaCovered = new byte[database.SectorCount];
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

            _bubbleList.Clear();
            _totalBubbles = 0;
            _gappedBubbles = 0;
            _bubblesCovered = new byte[database.SectorCount];

            for (int i = 1; i <= database.SectorCount; i++)
            {
                var s = database.LoadSector(i);
                if (s == null)
                    continue;

                if (s.Warp[1] > 0 && _bubblesCovered[i - 1] == 0)
                {
                    CheckBubble(i, s.Warp[0]);
                    CheckBubble(i, s.Warp[1]);

                    if (s.Warp[2] > 0)
                        CheckBubble(i, s.Warp[2]);
                    if (s.Warp[3] > 0)
                        CheckBubble(i, s.Warp[3]);
                    if (s.Warp[4] > 0)
                        CheckBubble(i, s.Warp[4]);
                    if (s.Warp[5] > 0)
                        CheckBubble(i, s.Warp[5]);
                }
            }

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

            _bubbleList.Clear();
            _totalBubbles = 0;
            _gappedBubbles = 0;
            _bubblesCovered = new byte[database.SectorCount];

            // Find all bubbles
            for (int i = 1; i <= database.SectorCount; i++)
            {
                var s = database.LoadSector(i);
                if (s == null)
                    continue;

                if (s.Warp[1] > 0 && _bubblesCovered[i - 1] == 0)
                {
                    CheckBubble(i, s.Warp[0]);
                    CheckBubble(i, s.Warp[1]);

                    if (s.Warp[2] > 0)
                        CheckBubble(i, s.Warp[2]);
                    if (s.Warp[3] > 0)
                        CheckBubble(i, s.Warp[3]);
                    if (s.Warp[4] > 0)
                        CheckBubble(i, s.Warp[4]);
                    if (s.Warp[5] > 0)
                        CheckBubble(i, s.Warp[5]);
                }
            }

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

        private void CheckBubble(int gate, ushort interior)
        {
            int size = TestBubble((ushort)gate, interior, out ushort deepest, out bool gapped);

            if (size > 1)
            {
                _bubbleList.Add(new Bubble
                {
                    Gate = (ushort)gate,
                    Deepest = deepest,
                    Size = (ushort)size,
                    Gapped = gapped
                });
            }
        }

        #endregion
    }
}
