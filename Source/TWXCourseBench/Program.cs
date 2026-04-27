using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TWXProxy.Core;

namespace TWXCourseBench;

internal static class Program
{
    private const int DefaultSamples = 10000;
    private const int DefaultSeed = 12345;
    private const int DefaultDiffExamples = 5;

    private static int Main(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            List<string> databasePaths = ResolveDatabasePaths(options.DatabasePaths);
            if (databasePaths.Count == 0)
            {
                Console.Error.WriteLine("No .xdb files found to benchmark.");
                return 1;
            }

            foreach (string dbPath in databasePaths)
            {
                BenchmarkDatabase(dbPath, options.Samples, options.Seed, options.DiffExamples);
                Console.WriteLine();
            }

            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            PrintUsage();
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Benchmark failed: {ex}");
            return 1;
        }
    }

    private static void BenchmarkDatabase(string dbPath, int sampleCount, int seed, int diffExamples)
    {
        using var db = new ModDatabase { ProgramDir = SharedPaths.ProgramDir };
        db.OpenDatabase(dbPath);
        int sectorCount = NormalizeDatabaseUniverseSize(db);
        Console.WriteLine($"effective sector count: {sectorCount}");
        var graph = PreparedGraph.FromDatabase(db);
        var hotRunner = new PascalHotRunner(graph);
        var candidates = Enumerable.Range(1, sectorCount)
            .Where(i => graph.Outbound[i].Length > 0)
            .ToArray();

        if (candidates.Length < 2)
        {
            Console.WriteLine($"DB={db.DatabaseName} path={dbPath}");
            Console.WriteLine("  insufficient connected sectors to benchmark");
            return;
        }

        var rng = new Random(unchecked(seed + db.DatabaseName.GetHashCode()));
        var pairs = new List<(int From, int To)>(sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            int from = candidates[rng.Next(candidates.Length)];
            int to = candidates[rng.Next(candidates.Length)];
            while (to == from)
                to = candidates[rng.Next(candidates.Length)];

            pairs.Add((from, to));
        }

        WarmUp(db, hotRunner, pairs);

        var (pascalMs, pascalResults) = TimeAlgorithm(pairs, pair => db.CalculatePascalShortestPath(pair.From, pair.To));
        var (hotMs, hotResults) = TimeAlgorithm(pairs, pair => hotRunner.FindPath(pair.From, pair.To));
        var (bidirectionalMs, bidirectionalResults) = TimeAlgorithm(pairs, pair => db.CalculateBidirectionalShortestPath(pair.From, pair.To));
        var (dijkstraMs, dijkstraResults) = TimeAlgorithm(pairs, pair => db.CalculateShortestPath(pair.From, pair.To));

        ComparisonStats hotStats = CompareAgainstBaseline("hot-bfs", pairs, pascalResults, hotResults, diffExamples);
        ComparisonStats bidirectionalStats = CompareAgainstBaseline("bidirectional-bfs", pairs, pascalResults, bidirectionalResults, diffExamples);
        ComparisonStats dijkstraStats = CompareAgainstBaseline("dijkstra", pairs, pascalResults, dijkstraResults, diffExamples);

        Console.WriteLine($"DB={db.DatabaseName} path={dbPath}");
        Console.WriteLine($"  candidate sectors with warps: {candidates.Length}");
        Console.WriteLine($"  sampled pairs: {pairs.Count}");
        Console.WriteLine($"  runtime pascal bfs: {pascalMs:F2} ms ({pascalMs / pairs.Count:F4} ms/query)");
        Console.WriteLine($"  hot bfs prototype:  {hotMs:F2} ms ({hotMs / pairs.Count:F4} ms/query) [{DescribeRelative(hotMs, pascalMs)}]");
        PrintComparison(hotStats);
        Console.WriteLine($"  bidirectional bfs:  {bidirectionalMs:F2} ms ({bidirectionalMs / pairs.Count:F4} ms/query) [{DescribeRelative(bidirectionalMs, pascalMs)}]");
        PrintComparison(bidirectionalStats);
        Console.WriteLine($"  dijkstra:           {dijkstraMs:F2} ms ({dijkstraMs / pairs.Count:F4} ms/query) [{DescribeRelative(dijkstraMs, pascalMs)}]");
        PrintComparison(dijkstraStats);
    }

    private static void WarmUp(
        ModDatabase db,
        PascalHotRunner hotRunner,
        IReadOnlyList<(int From, int To)> pairs)
    {
        int count = Math.Min(100, pairs.Count);
        for (int i = 0; i < count; i++)
        {
            var pair = pairs[i];
            _ = db.CalculatePascalShortestPath(pair.From, pair.To);
            _ = hotRunner.FindPath(pair.From, pair.To);
            _ = db.CalculateBidirectionalShortestPath(pair.From, pair.To);
            _ = db.CalculateShortestPath(pair.From, pair.To);
        }
    }

    private static (double ElapsedMs, List<List<int>> Results) TimeAlgorithm(
        IReadOnlyList<(int From, int To)> pairs,
        Func<(int From, int To), List<int>> algorithm)
    {
        var results = new List<List<int>>(pairs.Count);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < pairs.Count; i++)
            results.Add(algorithm(pairs[i]));
        sw.Stop();
        return (sw.Elapsed.TotalMilliseconds, results);
    }

    private static bool PathsEqual(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }

    private static string PathToString(IReadOnlyList<int> path)
        => path.Count == 0 ? "<none>" : string.Join(">", path.Select(v => v.ToString(CultureInfo.InvariantCulture)));

    private static void AddExample(List<string> examples, int limit, string example)
    {
        if (examples.Count < limit)
            examples.Add(example);
    }

    private static int NormalizeDatabaseUniverseSize(ModDatabase db)
    {
        int maxSector = GetLoadedMaxSector(db);
        if (db.DBHeader.Sectors >= maxSector && db.DBHeader.Sectors > 0)
            return db.DBHeader.Sectors;

        var header = db.DBHeader;
        header.Sectors = maxSector;
        db.UpdateHeader(header);
        return maxSector;
    }

    private static int GetLoadedMaxSector(ModDatabase db)
    {
        var sectorsField = typeof(ModDatabase).GetField("_sectors", BindingFlags.Instance | BindingFlags.NonPublic);
        if (sectorsField?.GetValue(db) is not ConcurrentDictionary<int, SectorData> sectors || sectors.IsEmpty)
            throw new InvalidOperationException("Could not determine loaded sector range for benchmark.");

        return sectors.Keys.Max();
    }

    private static ComparisonStats CompareAgainstBaseline(
        string name,
        IReadOnlyList<(int From, int To)> pairs,
        IReadOnlyList<List<int>> baselineResults,
        IReadOnlyList<List<int>> candidateResults,
        int diffExamples)
    {
        var stats = new ComparisonStats(name);
        for (int i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
            List<int> baseline = baselineResults[i];
            List<int> candidate = candidateResults[i];

            if (PathsEqual(baseline, candidate))
                stats.ExactSamePath++;

            bool baselineReachable = baseline.Count > 0;
            bool candidateReachable = candidate.Count > 0;

            if (!baselineReachable && !candidateReachable)
            {
                stats.BothUnreachable++;
                continue;
            }

            if (baselineReachable && !candidateReachable)
            {
                stats.BaselineOnlyReachable++;
                AddExample(stats.Examples, diffExamples,
                    $"reachable mismatch {pair.From}->{pair.To}: baseline={PathToString(baseline)} candidate=<none>");
                continue;
            }

            if (!baselineReachable && candidateReachable)
            {
                stats.CandidateOnlyReachable++;
                AddExample(stats.Examples, diffExamples,
                    $"reachable mismatch {pair.From}->{pair.To}: baseline=<none> candidate={PathToString(candidate)}");
                continue;
            }

            int baselineHops = baseline.Count - 1;
            int candidateHops = candidate.Count - 1;

            if (baselineHops == candidateHops)
            {
                stats.SameHopCount++;
                if (!PathsEqual(baseline, candidate))
                {
                    AddExample(stats.Examples, diffExamples,
                        $"same distance diff route {pair.From}->{pair.To}: baseline={PathToString(baseline)} candidate={PathToString(candidate)}");
                }
            }
            else
            {
                stats.DifferentHopCount++;
                AddExample(stats.Examples, diffExamples,
                    $"different distance {pair.From}->{pair.To}: baseline={PathToString(baseline)} ({baselineHops}) candidate={PathToString(candidate)} ({candidateHops})");
            }
        }

        return stats;
    }

    private static string DescribeRelative(double candidateMs, double baselineMs)
    {
        if (candidateMs == 0)
            return "n/a";

        if (candidateMs < baselineMs)
            return $"{baselineMs / candidateMs:F2}x faster than runtime";

        if (candidateMs > baselineMs)
            return $"{candidateMs / baselineMs:F2}x slower than runtime";

        return "same speed as runtime";
    }

    private static void PrintComparison(ComparisonStats stats)
    {
        Console.WriteLine($"    exact same path: {stats.ExactSamePath}");
        Console.WriteLine($"    same hop count: {stats.SameHopCount}");
        Console.WriteLine($"    different hop count: {stats.DifferentHopCount}");
        Console.WriteLine($"    both unreachable: {stats.BothUnreachable}");
        Console.WriteLine($"    baseline-only reachable: {stats.BaselineOnlyReachable}");
        Console.WriteLine($"    candidate-only reachable: {stats.CandidateOnlyReachable}");
        Console.WriteLine($"    sample diffs for {stats.Name}:");
        if (stats.Examples.Count == 0)
            Console.WriteLine("      <none>");
        else
            foreach (string example in stats.Examples)
                Console.WriteLine($"      {example}");
    }

    private static List<string> ResolveDatabasePaths(List<string> rawPaths)
    {
        if (rawPaths.Count == 0)
        {
            string dbDir = SharedPaths.DatabaseDir;
            return Directory.Exists(dbDir)
                ? Directory.GetFiles(dbDir, "*.xdb").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
        }

        var resolved = new List<string>();
        foreach (string raw in rawPaths)
        {
            string path = Path.GetFullPath(raw);
            if (!File.Exists(path))
                throw new ArgumentException($"Database file not found: {raw}");

            resolved.Add(path);
        }

        return resolved;
    }

    private static Options ParseArgs(string[] args)
    {
        var options = new Options();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;

                case "--samples":
                    options.Samples = ParsePositiveInt(args, ref i, "--samples");
                    break;

                case "--seed":
                    options.Seed = ParseInt(args, ref i, "--seed");
                    break;

                case "--show-diffs":
                    options.DiffExamples = ParseNonNegativeInt(args, ref i, "--show-diffs");
                    break;

                default:
                    options.DatabasePaths.Add(arg);
                    break;
            }
        }

        return options;
    }

    private static int ParsePositiveInt(string[] args, ref int index, string option)
    {
        int value = ParseInt(args, ref index, option);
        if (value <= 0)
            throw new ArgumentException($"{option} must be greater than 0.");
        return value;
    }

    private static int ParseNonNegativeInt(string[] args, ref int index, string option)
    {
        int value = ParseInt(args, ref index, option);
        if (value < 0)
            throw new ArgumentException($"{option} must be 0 or greater.");
        return value;
    }

    private static int ParseInt(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {option}.");

        index++;
        if (!int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new ArgumentException($"Invalid integer for {option}: {args[index]}");

        return value;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("TWXCourseBench");
        Console.WriteLine("Benchmarks GETCOURSE-style Pascal BFS against GETCOURSEDIJKSTRA-style shortest-path routing.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project Source/TWXCourseBench/TWXCourseBench.csproj -- [options] [db1.xdb db2.xdb ...]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine($"  --samples <n>      Random start/destination pairs per database (default {DefaultSamples})");
        Console.WriteLine($"  --seed <n>         Random seed base (default {DefaultSeed})");
        Console.WriteLine($"  --show-diffs <n>   Number of sample route differences to print (default {DefaultDiffExamples})");
        Console.WriteLine("  --help             Show this help");
        Console.WriteLine();
        Console.WriteLine("If no database paths are provided, the tool scans SharedPaths.DatabaseDir for *.xdb files.");
    }

    private sealed class Options
    {
        public bool ShowHelp { get; set; }
        public int Samples { get; set; } = DefaultSamples;
        public int Seed { get; set; } = DefaultSeed;
        public int DiffExamples { get; set; } = DefaultDiffExamples;
        public List<string> DatabasePaths { get; } = new();
    }

    private sealed class ComparisonStats
    {
        public ComparisonStats(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public int ExactSamePath { get; set; }
        public int SameHopCount { get; set; }
        public int DifferentHopCount { get; set; }
        public int BothUnreachable { get; set; }
        public int BaselineOnlyReachable { get; set; }
        public int CandidateOnlyReachable { get; set; }
        public List<string> Examples { get; } = new();
    }

    private sealed class PreparedGraph
    {
        private PreparedGraph(int sectorCount, int[][] outbound, int[][] inbound)
        {
            SectorCount = sectorCount;
            Outbound = outbound;
            Inbound = inbound;
        }

        public int SectorCount { get; }
        public int[][] Outbound { get; }
        public int[][] Inbound { get; }

        public static PreparedGraph FromDatabase(ModDatabase db)
        {
            int sectorCount = db.SectorCount;
            var outbound = new int[sectorCount + 1][];
            var inboundLists = new List<int>[sectorCount + 1];

            for (int sectorNumber = 1; sectorNumber <= sectorCount; sectorNumber++)
            {
                var sector = db.GetSector(sectorNumber);
                if (sector == null)
                {
                    outbound[sectorNumber] = Array.Empty<int>();
                    continue;
                }

                var warps = sector.Warp
                    .Where(warp => warp > 0 && warp <= sectorCount)
                    .Select(warp => (int)warp)
                    .ToArray();

                outbound[sectorNumber] = warps;
                foreach (int warp in warps)
                {
                    inboundLists[warp] ??= new List<int>(4);
                    inboundLists[warp].Add(sectorNumber);
                }
            }

            var inbound = new int[sectorCount + 1][];
            for (int sectorNumber = 1; sectorNumber <= sectorCount; sectorNumber++)
                inbound[sectorNumber] = inboundLists[sectorNumber]?.ToArray() ?? Array.Empty<int>();

            return new PreparedGraph(sectorCount, outbound, inbound);
        }
    }

    private sealed class PascalHotRunner
    {
        private readonly PreparedGraph _graph;
        private readonly int[] _visitStamp;
        private readonly int[] _previous;
        private readonly int[] _queue;
        private int _stamp;

        public PascalHotRunner(PreparedGraph graph)
        {
            _graph = graph;
            _visitStamp = new int[graph.SectorCount + 1];
            _previous = new int[graph.SectorCount + 1];
            _queue = new int[graph.SectorCount + 1];
        }

        public List<int> FindPath(int fromSector, int toSector)
        {
            if (fromSector < 1 || fromSector > _graph.SectorCount ||
                toSector < 1 || toSector > _graph.SectorCount)
            {
                return new List<int>();
            }

            if (fromSector == toSector)
                return new List<int> { fromSector };

            int stamp = NextStamp();
            int head = 0;
            int tail = 0;

            _visitStamp[fromSector] = stamp;
            _previous[fromSector] = 0;
            _queue[tail++] = fromSector;

            while (head < tail)
            {
                int focus = _queue[head++];
                int[] warps = _graph.Outbound[focus];
                for (int i = 0; i < warps.Length; i++)
                {
                    int adjacent = warps[i];
                    if (_visitStamp[adjacent] == stamp)
                        continue;

                    _previous[adjacent] = focus;
                    if (adjacent == toSector)
                        return ReconstructPath(fromSector, adjacent);

                    _visitStamp[adjacent] = stamp;
                    _queue[tail++] = adjacent;
                }
            }

            return new List<int>();
        }

        private List<int> ReconstructPath(int fromSector, int toSector)
        {
            var result = new List<int>();
            int current = toSector;
            while (current > 0)
            {
                result.Add(current);
                current = _previous[current];
            }

            result.Reverse();
            return result.Count > 0 && result[0] == fromSector ? result : new List<int>();
        }

        private int NextStamp()
        {
            _stamp++;
            if (_stamp != int.MaxValue)
                return _stamp;

            Array.Clear(_visitStamp, 0, _visitStamp.Length);
            _stamp = 1;
            return _stamp;
        }
    }

    private sealed class BidirectionalRunner
    {
        private readonly PreparedGraph _graph;
        private readonly int[] _visitStampForward;
        private readonly int[] _visitStampBackward;
        private readonly int[] _distanceForward;
        private readonly int[] _distanceBackward;
        private readonly int[] _previousForward;
        private readonly int[] _nextBackward;
        private readonly int[] _queueForward;
        private readonly int[] _queueBackward;
        private int _stamp;

        public BidirectionalRunner(PreparedGraph graph)
        {
            _graph = graph;
            int size = graph.SectorCount + 1;
            _visitStampForward = new int[size];
            _visitStampBackward = new int[size];
            _distanceForward = new int[size];
            _distanceBackward = new int[size];
            _previousForward = new int[size];
            _nextBackward = new int[size];
            _queueForward = new int[size];
            _queueBackward = new int[size];
        }

        public List<int> FindPath(int fromSector, int toSector)
        {
            if (fromSector < 1 || fromSector > _graph.SectorCount ||
                toSector < 1 || toSector > _graph.SectorCount)
            {
                return new List<int>();
            }

            if (fromSector == toSector)
                return new List<int> { fromSector };

            int stamp = NextStamp();
            int headForward = 0;
            int tailForward = 0;
            int headBackward = 0;
            int tailBackward = 0;
            int bestMeet = 0;
            int bestDistance = int.MaxValue;

            _visitStampForward[fromSector] = stamp;
            _distanceForward[fromSector] = 0;
            _previousForward[fromSector] = 0;
            _queueForward[tailForward++] = fromSector;

            _visitStampBackward[toSector] = stamp;
            _distanceBackward[toSector] = 0;
            _nextBackward[toSector] = 0;
            _queueBackward[tailBackward++] = toSector;

            while (headForward < tailForward && headBackward < tailBackward)
            {
                if ((tailForward - headForward) <= (tailBackward - headBackward))
                {
                    ExpandForwardLevel(stamp, ref headForward, ref tailForward, ref bestMeet, ref bestDistance);
                }
                else
                {
                    ExpandBackwardLevel(stamp, ref headBackward, ref tailBackward, ref bestMeet, ref bestDistance);
                }

                if (bestMeet == 0)
                    continue;

                int forwardFront = headForward < tailForward ? _distanceForward[_queueForward[headForward]] : int.MaxValue / 4;
                int backwardFront = headBackward < tailBackward ? _distanceBackward[_queueBackward[headBackward]] : int.MaxValue / 4;
                if ((long)forwardFront + backwardFront >= bestDistance)
                    break;
            }

            return bestMeet == 0 ? new List<int>() : ReconstructPath(fromSector, toSector, bestMeet);
        }

        private void ExpandForwardLevel(int stamp, ref int head, ref int tail, ref int bestMeet, ref int bestDistance)
        {
            int levelDistance = _distanceForward[_queueForward[head]];
            while (head < tail && _distanceForward[_queueForward[head]] == levelDistance)
            {
                int current = _queueForward[head++];
                int[] warps = _graph.Outbound[current];
                for (int i = 0; i < warps.Length; i++)
                {
                    int adjacent = warps[i];
                    if (_visitStampForward[adjacent] == stamp)
                        continue;

                    _visitStampForward[adjacent] = stamp;
                    _distanceForward[adjacent] = levelDistance + 1;
                    _previousForward[adjacent] = current;
                    _queueForward[tail++] = adjacent;

                    if (_visitStampBackward[adjacent] == stamp)
                    {
                        int totalDistance = _distanceForward[adjacent] + _distanceBackward[adjacent];
                        if (totalDistance < bestDistance)
                        {
                            bestDistance = totalDistance;
                            bestMeet = adjacent;
                        }
                    }
                }
            }
        }

        private void ExpandBackwardLevel(int stamp, ref int head, ref int tail, ref int bestMeet, ref int bestDistance)
        {
            int levelDistance = _distanceBackward[_queueBackward[head]];
            while (head < tail && _distanceBackward[_queueBackward[head]] == levelDistance)
            {
                int current = _queueBackward[head++];
                int[] inbound = _graph.Inbound[current];
                for (int i = 0; i < inbound.Length; i++)
                {
                    int previous = inbound[i];
                    if (_visitStampBackward[previous] == stamp)
                        continue;

                    _visitStampBackward[previous] = stamp;
                    _distanceBackward[previous] = levelDistance + 1;
                    _nextBackward[previous] = current;
                    _queueBackward[tail++] = previous;

                    if (_visitStampForward[previous] == stamp)
                    {
                        int totalDistance = _distanceForward[previous] + _distanceBackward[previous];
                        if (totalDistance < bestDistance)
                        {
                            bestDistance = totalDistance;
                            bestMeet = previous;
                        }
                    }
                }
            }
        }

        private List<int> ReconstructPath(int fromSector, int toSector, int meetSector)
        {
            var result = new List<int>();
            int current = meetSector;
            while (current > 0)
            {
                result.Add(current);
                current = _previousForward[current];
            }

            result.Reverse();
            if (result.Count == 0 || result[0] != fromSector)
                return new List<int>();

            current = _nextBackward[meetSector];
            while (current > 0)
            {
                result.Add(current);
                current = _nextBackward[current];
            }

            return result.Count > 0 && result[^1] == toSector ? result : new List<int>();
        }

        private int NextStamp()
        {
            _stamp++;
            if (_stamp != int.MaxValue)
                return _stamp;

            Array.Clear(_visitStampForward, 0, _visitStampForward.Length);
            Array.Clear(_visitStampBackward, 0, _visitStampBackward.Length);
            _stamp = 1;
            return _stamp;
        }
    }
}
