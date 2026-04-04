/*
Copyright (C) 2005  Remco Mulder, 2026 Matt Mosley

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation; either version 2 of the License, or
(at your option) any later version.
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace TWXProxy.Core
{
    public partial class ScriptRef
    {
        // Reference to the active database instance
        // TODO: This should be injected or accessed through a service locator
        private static ModDatabase? _activeDatabase;

        /// <summary>Exposes the active database for other components such as AutoRecorder.</summary>
        internal static ModDatabase? ActiveDatabase => _activeDatabase;
        
        // Sector avoid list
        private static readonly HashSet<int> _avoidedSectors = new();
        
        // Current sector tracking (set by game state or scripts)
        private static int _currentSector = 0;

        #region Database Command Implementation

        private static CmdAction CmdGetSector_Impl(object script, CmdParam[] parameters)
        {
            // CMD: getsector <sectorNum> var
            // Populates var with struct-like sub-fields for the given sector.
            // The base variable value is NOT modified (it retains its current value,
            // which is typically the sector number).
            // Sub-fields populated: density, warps, warp[1..n], anomoly, port.exists,
            // port.class, figs.owner, figs.quantity, figs.type, etc.
            
            if (parameters[1] is not VarParam sectorVar)
                return CmdAction.None;

            int sectorNum;
            ConvertToNumber(parameters[0].Value, out sectorNum);

            // Helper to set a named sub-field.
            // Names with dots (e.g. "figs.owner") are stored hierarchically so that
            // compiled dot-notation ($thisSector.figs.owner → $thisSector["figs"]["owner"])
            // resolves correctly at runtime.
            void SetField(string name, string value)
            {
                var parts = name.Split('.');
                sectorVar.GetIndexVar(parts).Value = value;

                if (script is Script scriptObj && scriptObj.Compiler != null)
                {
                    string flatName = sectorVar.Name + "." + name;
                    scriptObj.Compiler.GetOrCreateRuntimeVar(flatName).Value = value;
                }
            }

            static bool ShouldTraceSectorVar(VarParam varParam)
            {
                string name = varParam.Name ?? string.Empty;
                return name.IndexOf("CURSECTOR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       name.IndexOf("THISSECTOR", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (_activeDatabase == null || sectorNum <= 0 || sectorNum > _activeDatabase.SectorCount)
            {
                // Unknown sector – zero out all commonly-used fields
                SetField("density",       "0");
                SetField("navhaz",        "0");
                SetField("anomoly",       "NO");
                SetField("warps",         "0");
                SetField("port.exists",   "0");
                SetField("port.class",    "0");
                SetField("figs.owner",    "");
                SetField("figs.quantity", "0");
                SetField("figs.type",     "0");
                // Clear warp array (at least first few slots)
                for (int i = 1; i <= 6; i++)
                    sectorVar.GetIndexVar(new[] { "warp", i.ToString() }).Value = "0";
                return CmdAction.None;
            }

            var sector = _activeDatabase.GetSector(sectorNum);
            if (sector == null)
            {
                SetField("density",       "0");
                SetField("navhaz",        "0");
                SetField("anomoly",       "NO");
                SetField("warps",         "0");
                SetField("port.exists",   "0");
                SetField("port.class",    "0");
                SetField("figs.owner",    "");
                SetField("figs.quantity", "0");
                SetField("figs.type",     "0");
                for (int i = 1; i <= 6; i++)
                    sectorVar.GetIndexVar(new[] { "warp", i.ToString() }).Value = "0";
                return CmdAction.None;
            }

            // Populate sector fields
            SetField("density",  sector.Density.ToString());
            SetField("navhaz",   sector.NavHaz.ToString());
            SetField("anomoly",  sector.Anomaly ? "YES" : "NO");

            var warps = sector.Warp.Where(w => w != 0).ToList();
            SetField("warps", warps.Count.ToString());
            for (int i = 1; i <= 6; i++)
            {
                string warpValue = (i <= warps.Count) ? warps[i - 1].ToString() : "0";
                sectorVar.GetIndexVar(new[] { "warp", i.ToString() }).Value = warpValue;

                if (script is Script scriptObj && scriptObj.Compiler != null)
                {
                    scriptObj.Compiler
                        .GetOrCreateRuntimeVar(sectorVar.Name + ".warp")
                        .GetIndexVar(new[] { i.ToString() })
                        .Value = warpValue;
                }
            }

            // Port
            bool portExists = sector.SectorPort != null;
            SetField("port.exists", portExists ? "1" : "0");
            SetField("port.class",  sector.SectorPort?.ClassIndex.ToString() ?? "0");
            SetField("port.name",   sector.SectorPort?.Name ?? "");
            if (sector.SectorPort != null)
            {
                SetField("port.fuel",        sector.SectorPort.ProductAmount.GetValueOrDefault(ProductType.FuelOre).ToString());
                SetField("port.org",         sector.SectorPort.ProductAmount.GetValueOrDefault(ProductType.Organics).ToString());
                SetField("port.equip",       sector.SectorPort.ProductAmount.GetValueOrDefault(ProductType.Equipment).ToString());
                SetField("port.buyfuel",     sector.SectorPort.BuyProduct.GetValueOrDefault(ProductType.FuelOre) ? "1" : "0");
                SetField("port.buyorg",      sector.SectorPort.BuyProduct.GetValueOrDefault(ProductType.Organics) ? "1" : "0");
                SetField("port.buyequip",    sector.SectorPort.BuyProduct.GetValueOrDefault(ProductType.Equipment) ? "1" : "0");
            }

            // Fighters
            SetField("figs.owner",    sector.Fighters.Owner ?? "");
            SetField("figs.quantity", sector.Fighters.Quantity.ToString());
            SetField("figs.type",     sector.Fighters.FigType.ToString());

            // Mines / Limpets
            SetField("limpets.owner",    sector.MinesArmid.Owner ?? "");
            SetField("limpets.quantity", sector.MinesArmid.Quantity.ToString());

            // Beacon / Constellation
            SetField("beacon",        sector.Beacon.ToString());
            SetField("constellation", sector.Constellation.ToString());

            if (GlobalModules.VerboseDebugMode)
                GlobalModules.DebugLog($"[GETSECTOR] #{sectorNum} warps:[{string.Join(",", warps)}] port.class={sector.SectorPort?.ClassIndex ?? 0}\n");

            if (ShouldTraceSectorVar(sectorVar))
            {
                string warp1 = sectorVar.GetIndexVar(new[] { "warp", "1" }).Value;
                string warp2 = sectorVar.GetIndexVar(new[] { "warp", "2" }).Value;
                string warp3 = sectorVar.GetIndexVar(new[] { "warp", "3" }).Value;
                GlobalModules.DebugLog(
                    $"[GETSECTOR TRACE] var='{sectorVar.Name}' sector={sectorNum} " +
                    $"warps={warps.Count} warp1='{warp1}' warp2='{warp2}' warp3='{warp3}' " +
                    $"density='{sector.Density}' portClass='{sector.SectorPort?.ClassIndex ?? 0}'\n");
            }

            return CmdAction.None;
        }

        private static CmdAction CmdGetSectorParameter_Impl(object script, CmdParam[] parameters)
        {
            // CMD: getsectorparameter <sector> <param> var          (3-param, C# form)
            //  or: getsectorparameter <sector> %%progvar             (2-param, Pascal form)
            // In the Pascal form the progvar NAME is the DB key and the result is stored back into it.
            
            int sectorNum;
            ConvertToNumber(parameters[0].Value, out sectorNum);

            bool twoParam = parameters.Length == 2 && parameters[1] is ProgVarParam;
            string paramName = twoParam
                ? ((ProgVarParam)parameters[1]).Name.ToUpperInvariant()
                : parameters[1].Value.ToUpperInvariant();
            CmdParam outputParam = twoParam ? parameters[1] : parameters[2];
            
            if (_activeDatabase == null || sectorNum <= 0 || sectorNum > _activeDatabase.SectorCount)
            {
                outputParam.Value = string.Empty;
                return CmdAction.None;
            }
            
            // Check custom variables first
            string varValue = _activeDatabase.GetSectorVar(sectorNum, paramName);
            if (!string.Empty.Equals(varValue))
            {
                outputParam.Value = varValue;
                return CmdAction.None;
            }
            
            var sector = _activeDatabase.GetSector(sectorNum);
            if (sector == null)
            {
                outputParam.Value = string.Empty;
                return CmdAction.None;
            }
            
            // Map parameter names to sector properties
            // Convert Warp array to list of warps
            var warpList = sector.Warp.Where(w => w != 0).ToList();
            
            outputParam.Value = paramName switch
            {
                "WARPS" => string.Join(" ", warpList),
                "WARPCOUNT" => warpList.Count.ToString(),
                "WARPIN" or "WARPSIN" => string.Join(" ", sector.WarpsIn),
                "WARPINCOUNT" => sector.WarpsIn.Count.ToString(),
                "BEACON" => sector.Beacon.ToString(),
                "CONSTELLATION" => sector.Constellation.ToString(),
                "EXPLORED" => (sector.Explored != ExploreType.No) ? "YES" : "NO",
                "DENSITY" => sector.Density.ToString(),
                "NAVHAZ" => sector.NavHaz.ToString(),
                "ANOMALY" or "ANOMOLY" => sector.Anomaly ? "1" : "0",
                "DEADEND" => (warpList.Count == 1) ? "1" : "0",
                "BACKDOORCOUNT" => _activeDatabase.GetBackDoors(sector, sectorNum).Count.ToString(),
                "BACKDOORS" => string.Join(" ", _activeDatabase.GetBackDoors(sector, sectorNum)),
                "PORT.EXISTS" => (sector.SectorPort != null) ? "1" : "0",
                "PORT.CLASS" => sector.SectorPort?.ClassIndex.ToString() ?? "0",
                "PORT.NAME" => sector.SectorPort?.Name ?? string.Empty,
                "PORT.FUEL" => sector.SectorPort?.ProductAmount.GetValueOrDefault(ProductType.FuelOre).ToString() ?? "0",
                "PORT.ORG" => sector.SectorPort?.ProductAmount.GetValueOrDefault(ProductType.Organics).ToString() ?? "0",
                "PORT.EQUIP" => sector.SectorPort?.ProductAmount.GetValueOrDefault(ProductType.Equipment).ToString() ?? "0",
                "PORT.BUYFUEL" => (sector.SectorPort?.BuyProduct.GetValueOrDefault(ProductType.FuelOre) == true) ? "1" : "0",
                "PORT.BUYORG" => (sector.SectorPort?.BuyProduct.GetValueOrDefault(ProductType.Organics) == true) ? "1" : "0",
                "PORT.BUYEQUIP" => (sector.SectorPort?.BuyProduct.GetValueOrDefault(ProductType.Equipment) == true) ? "1" : "0",
                "PORT.PERCENTFUEL" => sector.SectorPort?.ProductPercent.GetValueOrDefault(ProductType.FuelOre).ToString() ?? "0",
                "PORT.PERCENTORG" => sector.SectorPort?.ProductPercent.GetValueOrDefault(ProductType.Organics).ToString() ?? "0",
                "PORT.PERCENTEQUIP" => sector.SectorPort?.ProductPercent.GetValueOrDefault(ProductType.Equipment).ToString() ?? "0",
                "PORT.BUILDTIME" => sector.SectorPort?.BuildTime.ToString() ?? string.Empty,
                "PORT.UPDATED" => sector.SectorPort?.Update.ToString() ?? string.Empty,
                "SHIPS" => sector.Ships.Count > 0 ? string.Join(", ", sector.Ships.Select(s => s.Name)) : string.Empty,
                "SHIPCOUNT" => sector.Ships.Count.ToString(),
                "PLANETS" => sector.PlanetNames.Count > 0 ? string.Join(", ", sector.PlanetNames) : string.Empty,
                "PLANETCOUNT" => sector.PlanetNames.Count.ToString(),
                "TRADERS" => sector.Traders.Count > 0 ? string.Join(", ", sector.Traders.Select(t => t.Name)) : string.Empty,
                "TRADERCOUNT" => sector.Traders.Count.ToString(),
                "FIGS.OWNER" => sector.Fighters.Owner,
                "FIGS.QUANTITY" => sector.Fighters.Quantity.ToString(),
                "FIGS.TYPE" => sector.Fighters.FigType.ToString(),
                "MINES.OWNER" => string.Empty, // No Mines property in SectorData - only MinesArmid and MinesLimpet
                "MINES.QUANTITY" => "0",
                "LIMPETS.OWNER" => sector.MinesArmid.Owner,
                "LIMPETS.QUANTITY" => sector.MinesArmid.Quantity.ToString(),
                "UPDATED" => sector.Update.ToString(),
                _ => varValue // Return custom variable or empty
            };
            
            return CmdAction.None;
        }

        private static CmdAction CmdSetSectorParameter_Impl(object script, CmdParam[] parameters)
        {
            // CMD: setsectorparameter <sector> <param> <value>     (3-param, C# form)
            //  or: setsectorparameter <sector> %%progvar            (2-param, Pascal form)
            // In the Pascal form the progvar NAME is the DB key and the progvar VALUE is stored.
            
            int sectorNum;
            ConvertToNumber(parameters[0].Value, out sectorNum);

            string paramName, value;
            if (parameters.Length == 2 && parameters[1] is ProgVarParam pgp)
            {
                paramName = pgp.Name;
                value = pgp.Value;
            }
            else
            {
                paramName = parameters[1].Value;
                value = parameters[2].Value;
            }
            
            if (_activeDatabase != null && sectorNum > 0 && sectorNum <= _activeDatabase.SectorCount)
            {
                _activeDatabase.SetSectorVar(sectorNum, paramName, value);
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdListSectorParameters_Impl(object script, CmdParam[] parameters)
        {
            // CMD: listsectorparameters <sector> var
            // List all custom parameters set for a sector
            
            int sectorNum;
            ConvertToNumber(parameters[0].Value, out sectorNum);
            
            if (parameters[1] is VarParam varParam)
            {
                if (_activeDatabase != null && sectorNum > 0 && sectorNum <= _activeDatabase.SectorCount)
                {
                    var paramNames = _activeDatabase.GetSectorVarNames(sectorNum);
                    varParam.SetArrayFromStrings(paramNames.ToList());
                }
                else
                {
                    varParam.SetArrayFromStrings(new List<string>());
                }
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdGetCourse_Impl(object script, CmdParam[] parameters)
        {
            // CMD: getcourse var <from> <to>
            // Calculate shortest path between two sectors
            
            int fromSector, toSector;
            ConvertToNumber(parameters[1].Value, out fromSector);
            ConvertToNumber(parameters[2].Value, out toSector);
            
            if (parameters[0] is VarParam varParam &&
                _activeDatabase != null &&
                fromSector > 0 && fromSector <= _activeDatabase.SectorCount &&
                toSector > 0 && toSector <= _activeDatabase.SectorCount)
            {
                var path = CalculatePath(fromSector, toSector);
                parameters[0].Value = Math.Max(0, path.Count - 1).ToString();

                if (path.Count > 0)
                {
                    path.Reverse(); // Pascal reverses PlotWarpCourse before exposing the array.
                    varParam.SetArrayFromStrings(path.Select(sector => sector.ToString()).ToList());
                }
                else
                {
                    varParam.SetArrayFromStrings(new List<string>());
                }
            }
            else
            {
                parameters[0].Value = "0";
                if (parameters[0] is VarParam emptyVarParam)
                    emptyVarParam.SetArrayFromStrings(new List<string>());
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdGetDistance_Impl(object script, CmdParam[] parameters)
        {
            // CMD: getdistance var <from> <to>
            // Calculate distance (number of warps) between two sectors
            
            int fromSector, toSector;
            ConvertToNumber(parameters[1].Value, out fromSector);
            ConvertToNumber(parameters[2].Value, out toSector);
            
            if (_activeDatabase != null && 
                fromSector > 0 && fromSector <= _activeDatabase.SectorCount &&
                toSector > 0 && toSector <= _activeDatabase.SectorCount)
            {
                var path = CalculatePath(fromSector, toSector);
                parameters[0].Value = (path.Count - 1).ToString(); // Distance is path length - 1
            }
            else
            {
                parameters[0].Value = "0";
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdGetAllCourses_Impl(object script, CmdParam[] parameters)
        {
            // CMD: getallcourses var <sector>
            // Get all possible routes from current sector to target with distances
            // Returns array of "warp:distance" strings sorted by distance
            
            int toSector;
            ConvertToNumber(parameters[1].Value, out toSector);
            
            if (parameters[0] is VarParam varParam)
            {
                if (_activeDatabase == null)
                {
                    varParam.SetArrayFromStrings(new List<string>());
                    return CmdAction.None;
                }

                int fromSector = _currentSector;
                
                // If current sector not set or invalid, return empty
                if (fromSector <= 0 || fromSector > _activeDatabase.SectorCount ||
                    toSector <= 0 || toSector > _activeDatabase.SectorCount)
                {
                    varParam.SetArrayFromStrings(new List<string>());
                    return CmdAction.None;
                }

                var sector = _activeDatabase.GetSector(fromSector);
                if (sector == null)
                {
                    varParam.SetArrayFromStrings(new List<string>());
                    return CmdAction.None;
                }

                // Calculate distance from each warp to target
                var warpRoutes = new List<(int warp, int distance)>();
                foreach (var warp in sector.Warp.Where(w => w > 0 && w <= _activeDatabase.SectorCount))
                {
                    // Skip avoided sectors
                    if (_avoidedSectors.Contains(warp))
                        continue;

                    int distance = _activeDatabase.GetDistance(warp, toSector, _avoidedSectors);
                    
                    // Include all warps, even if unreachable (distance = -1)
                    warpRoutes.Add((warp, distance));
                }

                // Sort by distance (unreachable paths at end)
                var sortedRoutes = warpRoutes
                    .OrderBy(x => x.distance < 0 ? int.MaxValue : x.distance)
                    .ThenBy(x => x.warp)
                    .Select(x => x.distance >= 0 ? $"{x.warp}:{x.distance + 1}" : $"{x.warp}:UNREACHABLE")
                    .ToList();

                varParam.SetArrayFromStrings(sortedRoutes);
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdGetNearestWarps_Impl(object script, CmdParam[] parameters)
        {
            // Pascal TWX semantics:
            //   getnearestwarps <ArrayName> <StartingSector>
            // Returns the breadth-first reachable-sector queue produced by PlotWarpCourse(start, 0).

            int startSector;
            ConvertToNumber(parameters[1].Value, out startSector);

            if (parameters[0] is VarParam varParam)
            {
                if (_activeDatabase != null &&
                    startSector > 0 && startSector <= _activeDatabase.SectorCount)
                {
                    var reachable = _activeDatabase.GetReachableSectorsBreadthFirst(startSector, _avoidedSectors)
                        .Select(sector => sector.ToString())
                        .ToList();
                    parameters[0].Value = reachable.Count.ToString();
                    varParam.SetArrayFromStrings(reachable);
                }
                else
                {
                    parameters[0].Value = "0";
                    varParam.SetArrayFromStrings(new List<string>());
                }
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdSetAvoid_Impl(object script, CmdParam[] parameters)
        {
            // CMD: setavoid <sector>
            // Mark sector as avoided in pathfinding
            
            int sectorNum;
            ConvertToNumber(parameters[0].Value, out sectorNum);
            
            if (_activeDatabase != null && sectorNum > 0 && sectorNum <= _activeDatabase.SectorCount)
            {
                _avoidedSectors.Add(sectorNum);
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdClearAvoid_Impl(object script, CmdParam[] parameters)
        {
            // CMD: clearavoid <sector>
            // Remove sector from avoid list
            
            int sectorNum;
            ConvertToNumber(parameters[0].Value, out sectorNum);
            _avoidedSectors.Remove(sectorNum);
            
            return CmdAction.None;
        }

        private static CmdAction CmdClearAllAvoids_Impl(object script, CmdParam[] parameters)
        {
            // CMD: clearallavoids
            // Clear all avoided sectors
            
            _avoidedSectors.Clear();
            return CmdAction.None;
        }

        private static CmdAction CmdListAvoids_Impl(object script, CmdParam[] parameters)
        {
            // CMD: listavoids var
            // List all avoided sectors
            
            if (parameters[0] is VarParam varParam)
            {
                var avoidList = _avoidedSectors.OrderBy(s => s).Select(s => s.ToString()).ToList();
                varParam.SetArrayFromStrings(avoidList);
            }
            
            return CmdAction.None;
        }

        #endregion

        #region Pathfinding Helper Methods

        private static List<int> CalculatePath(int fromSector, int toSector)
        {
            if (_activeDatabase == null)
                return new List<int>();
            
            if (fromSector == toSector)
                return new List<int> { fromSector };
            
            // Use Dijkstra pathfinding with avoided sectors
            return _activeDatabase.CalculateShortestPath(fromSector, toSector, _avoidedSectors);
        }

        #endregion

        #region Database Access Helper

        /// <summary>
        /// Set the active database instance for script commands
        /// This should be called when a database is opened
        /// </summary>
        public static void SetActiveDatabase(ModDatabase? database)
        {
            _activeDatabase = database;
            GlobalModules.TWXDatabase = database;

            if (GlobalModules.TWXLog is ModLog log)
                log.SetLogIdentity(database?.DatabaseName);
        }

        /// <summary>
        /// Set the current sector for navigation commands
        /// This should be called when game state changes or sector is detected
        /// </summary>
        public static void SetCurrentSector(int sectorNumber)
        {
            _currentSector = sectorNumber;
        }

        /// <summary>
        /// Get the current sector
        /// </summary>
        public static int GetCurrentSector()
        {
            return _currentSector;
        }

        /// <summary>
        /// Get the active database instance
        /// </summary>
        public static ModDatabase? GetActiveDatabase()
        {
            return _activeDatabase;
        }

        #endregion
    }
}
