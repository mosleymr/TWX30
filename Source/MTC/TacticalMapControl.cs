using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Core = TWXProxy.Core;

namespace MTC;

/// <summary>
/// Compact tactical map used by the optional "command deck" skin.
/// It follows the current sector and renders the nearby bubble as a
/// luminous node-link display rather than the larger standalone map window.
/// </summary>
public class TacticalMapControl : Control
{
    private const int MaxDepth = 3;
    private const float GridX = 110f;
    private const float GridY = 84f;
    private const float NodeRadius = 20f;

    private readonly Func<int> _getCurrentSector;
    private readonly Func<Core.ModDatabase?> _getDb;
    private readonly (float X, float Y, float Size, byte Alpha)[] _stars;

    public TacticalMapControl(Func<int> getCurrentSector, Func<Core.ModDatabase?> getDb)
    {
        _getCurrentSector = getCurrentSector;
        _getDb = getDb;
        ClipToBounds = true;

        var rng = new Random(1337);
        _stars = new (float, float, float, byte)[140];
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = (
                (float)rng.NextDouble(),
                (float)rng.NextDouble(),
                0.7f + (float)rng.NextDouble() * 1.8f,
                (byte)(50 + rng.Next(130)));
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.Custom(new TacticalDrawOp(bounds, this));
    }

    internal void Draw(SKCanvas canvas, float width, float height)
    {
        DrawBackdrop(canvas, width, height);

        MapSnapshot snapshot = BuildSnapshot();
        if (snapshot.Positions.Count == 0)
        {
            DrawEmptyState(canvas, width, height);
            return;
        }

        (float minX, float minY, float maxX, float maxY) = MeasureBounds(snapshot.Positions.Values);
        float contentWidth = Math.Max(1f, maxX - minX);
        float contentHeight = Math.Max(1f, maxY - minY);
        float scale = Math.Min((width - 90f) / contentWidth, (height - 90f) / contentHeight);
        scale = Math.Clamp(scale, 0.52f, 1.24f);

        canvas.Save();
        canvas.Translate(width / 2f, height / 2f);
        canvas.Scale(scale);
        canvas.Translate(-(minX + maxX) / 2f, -(minY + maxY) / 2f);

        DrawEdges(canvas, snapshot);
        DrawNodes(canvas, snapshot);

        canvas.Restore();
        DrawOverlayLegend(canvas, width, height, snapshot);
    }

    private void DrawBackdrop(SKCanvas canvas, float width, float height)
    {
        using var bgShader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            [
                new SKColor(0x04, 0x0c, 0x12),
                new SKColor(0x0b, 0x1b, 0x24),
                new SKColor(0x05, 0x12, 0x18),
            ],
            null,
            SKShaderTileMode.Clamp);
        using var bgPaint = new SKPaint { Shader = bgShader, Style = SKPaintStyle.Fill };
        canvas.DrawRect(0, 0, width, height, bgPaint);

        using var glow = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(width * 0.28f, height * 0.62f),
                Math.Max(width, height) * 0.45f,
                [
                    new SKColor(0x00, 0xc4, 0xc1, 45),
                    new SKColor(0x00, 0xc4, 0xc1, 0),
                ],
                null,
                SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(0, 0, width, height, glow);

        using var gridPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x2b, 0x60, 0x69, 36),
            StrokeWidth = 1f,
        };

        float spacing = 48f;
        for (float x = spacing; x < width; x += spacing)
            canvas.DrawLine(x, 0, x, height, gridPaint);
        for (float y = spacing; y < height; y += spacing)
            canvas.DrawLine(0, y, width, y, gridPaint);

        using var ringPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x8e, 0xd8, 0xd8, 26),
            StrokeWidth = 1.2f,
        };
        var center = new SKPoint(width * 0.5f, height * 0.56f);
        for (float radius = 58f; radius < Math.Max(width, height) * 0.48f; radius += 54f)
            canvas.DrawCircle(center, radius, ringPaint);

        using var starPaint = new SKPaint { IsAntialias = true };
        foreach (var star in _stars)
        {
            starPaint.Color = new SKColor(0xd6, 0xf5, 0xff, star.Alpha);
            canvas.DrawCircle(star.X * width, star.Y * height, star.Size, starPaint);
        }
    }

    private static void DrawEmptyState(SKCanvas canvas, float width, float height)
    {
        using var titlePaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x9c, 0xc6, 0xcf),
            TextSize = 18f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.Default,
        };
        using var bodyPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x6f, 0x95, 0x9d),
            TextSize = 13f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.Default,
        };

        canvas.DrawText("Awaiting sector telemetry", width / 2f, height / 2f - 10f, titlePaint);
        canvas.DrawText("Connect to a game to populate the tactical overlay.", width / 2f, height / 2f + 18f, bodyPaint);
    }

    private static void DrawEdges(SKCanvas canvas, MapSnapshot snapshot)
    {
        using var linkGlow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x00, 0xe8, 0xe0, 24),
            StrokeWidth = 7f,
        };
        using var linkPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x62, 0xbd, 0xc7, 150),
            StrokeWidth = 2f,
        };
        using var oneWayPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x6c, 0x8a, 0x98, 110),
            StrokeWidth = 1.4f,
            PathEffect = SKPathEffect.CreateDash([6f, 6f], 0),
        };

        var drawnEdges = new HashSet<(int A, int B)>();
        foreach ((int sectorNumber, SKPoint position) in snapshot.Positions)
        {
            if (!snapshot.Sectors.TryGetValue(sectorNumber, out Core.SectorData? sector) || sector == null)
                continue;

            foreach (ushort warpTarget in sector.Warp.Where(w => w > 0 && snapshot.Positions.ContainsKey(w)))
            {
                int targetSector = warpTarget;
                bool twoWay = snapshot.Sectors.TryGetValue(targetSector, out Core.SectorData? target) &&
                              target != null &&
                              target.Warp.Contains((ushort)sectorNumber);

                int a = Math.Min(sectorNumber, targetSector);
                int b = Math.Max(sectorNumber, targetSector);
                if (twoWay && !drawnEdges.Add((a, b)))
                    continue;

                SKPoint start = position;
                SKPoint end = snapshot.Positions[targetSector];
                canvas.DrawLine(start, end, linkGlow);
                canvas.DrawLine(start, end, twoWay ? linkPaint : oneWayPaint);
            }
        }
    }

    private static void DrawNodes(SKCanvas canvas, MapSnapshot snapshot)
    {
        using var nodeFill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var nodeGlow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var ringPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.Default,
        };
        using var smallPaint = new SKPaint
        {
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.Default,
        };

        foreach ((int sectorNumber, SKPoint position) in snapshot.Positions.OrderBy(kvp => snapshot.Depths.GetValueOrDefault(kvp.Key)))
        {
            snapshot.Sectors.TryGetValue(sectorNumber, out Core.SectorData? sector);

            bool isCurrent = sectorNumber == snapshot.CurrentSector;
            bool isLandmark = snapshot.Landmarks.Contains(sectorNumber);
            SKColor fillColor = isCurrent
                ? new SKColor(0xff, 0xc0, 0x54)
                : isLandmark
                    ? new SKColor(0x52, 0xa4, 0xff)
                    : sector?.Explored == Core.ExploreType.Yes
                        ? new SKColor(0x00, 0xd9, 0xd0)
                        : sector?.Explored == Core.ExploreType.Density
                            ? new SKColor(0x2b, 0x8e, 0x9d)
                            : new SKColor(0x3a, 0x4d, 0x57);

            nodeGlow.Color = fillColor.WithAlpha((byte)(isCurrent ? 90 : 42));
            canvas.DrawCircle(position, isCurrent ? NodeRadius + 10f : NodeRadius + 6f, nodeGlow);

            nodeFill.Color = fillColor;
            canvas.DrawCircle(position, isCurrent ? NodeRadius + 2f : NodeRadius, nodeFill);

            ringPaint.Color = isCurrent
                ? new SKColor(0xff, 0xe8, 0xa4)
                : isLandmark
                    ? new SKColor(0x8a, 0xc6, 0xff)
                    : new SKColor(0x73, 0xd4, 0xd9);
            canvas.DrawCircle(position, isCurrent ? NodeRadius + 4f : NodeRadius + 1.5f, ringPaint);

            textPaint.Color = fillColor.Red > 200 && fillColor.Green > 160
                ? new SKColor(0x0d, 0x18, 0x1f)
                : SKColors.White;
            canvas.DrawText(sectorNumber.ToString(), position.X, position.Y + 4f, textPaint);

            if (sector?.SectorPort != null && !sector.SectorPort.Dead)
            {
                smallPaint.Color = new SKColor(0x0d, 0x18, 0x1f);
                canvas.DrawCircle(position.X + NodeRadius - 4f, position.Y - NodeRadius + 4f, 5.5f, nodeFill);
                smallPaint.Color = new SKColor(0xff, 0xff, 0xff);
                canvas.DrawText("P", position.X + NodeRadius - 4f, position.Y - NodeRadius + 7f, smallPaint);
            }
        }
    }

    private static void DrawOverlayLegend(SKCanvas canvas, float width, float height, MapSnapshot snapshot)
    {
        string text = snapshot.CurrentSector > 0
            ? $"LIVE SECTOR {snapshot.CurrentSector}  |  {snapshot.Positions.Count} NODE VIEW"
            : "LIVE TACTICAL OVERLAY";

        using var overlayPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x07, 0x11, 0x18, 185),
            Style = SKPaintStyle.Fill,
        };
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0xd6, 0xf6, 0xf4),
            TextSize = 12f,
            Typeface = SKTypeface.Default,
        };

        canvas.DrawRoundRect(new SKRoundRect(new SKRect(16, height - 36, 240, height - 12), 8, 8), overlayPaint);
        canvas.DrawText(text, 28, height - 18, textPaint);
    }

    private MapSnapshot BuildSnapshot()
    {
        var snapshot = new MapSnapshot();
        Core.ModDatabase? db = _getDb();
        int currentSector = Math.Max(1, _getCurrentSector());
        snapshot.CurrentSector = currentSector;

        if (db == null || currentSector <= 0)
            return snapshot;

        var visited = new Dictionary<int, int> { [currentSector] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(currentSector);

        while (queue.Count > 0)
        {
            int sectorNumber = queue.Dequeue();
            int depth = visited[sectorNumber];
            Core.SectorData? sector = db.GetSector(sectorNumber);
            snapshot.Sectors[sectorNumber] = sector;

            if (sector == null || depth >= MaxDepth)
                continue;

            foreach (ushort warpTarget in sector.Warp.Where(w => w > 0))
            {
                if (visited.ContainsKey(warpTarget))
                    continue;

                visited[warpTarget] = depth + 1;
                queue.Enqueue(warpTarget);
            }

            foreach (ushort inbound in sector.WarpsIn.Where(w => w > 0))
            {
                if (visited.ContainsKey(inbound))
                    continue;

                visited[inbound] = depth + 1;
                queue.Enqueue(inbound);
            }
        }

        foreach (int sectorNumber in visited.Keys.Where(sectorNumber => !snapshot.Sectors.ContainsKey(sectorNumber)))
            snapshot.Sectors[sectorNumber] = db.GetSector(sectorNumber);

        snapshot.Depths = visited;
        snapshot.Positions = ComputePositions(db, currentSector, visited);

        var header = db.DBHeader;
        AddLandmark(snapshot.Landmarks, header.StarDock);
        AddLandmark(snapshot.Landmarks, header.Rylos);
        AddLandmark(snapshot.Landmarks, header.AlphaCentauri);

        return snapshot;
    }

    private static Dictionary<int, SKPoint> ComputePositions(Core.ModDatabase db, int centerSector, Dictionary<int, int> visited)
    {
        var cellOf = new Dictionary<int, (int Col, int Row)> { [centerSector] = (0, 0) };
        var usedCells = new HashSet<(int Col, int Row)> { (0, 0) };
        var parentOf = new Dictionary<int, int>();
        var placed = new HashSet<int> { centerSector };
        var placeQueue = new Queue<int>();
        placeQueue.Enqueue(centerSector);

        (int Col, int Row)[] offsets =
        [
            ( 1,  0), ( 1,  1), ( 0,  1), (-1,  1),
            (-1,  0), (-1, -1), ( 0, -1), ( 1, -1),
        ];

        var bfs = new Queue<int>();
        var seen = new HashSet<int> { centerSector };
        bfs.Enqueue(centerSector);
        while (bfs.Count > 0)
        {
            int sectorNumber = bfs.Dequeue();
            Core.SectorData? sector = db.GetSector(sectorNumber);
            if (sector == null)
                continue;

            foreach (ushort warpTarget in sector.Warp.Where(w => w > 0 && visited.ContainsKey(w) && !seen.Contains(w)))
            {
                seen.Add(warpTarget);
                parentOf[warpTarget] = sectorNumber;
                bfs.Enqueue(warpTarget);
            }
        }

        while (placeQueue.Count > 0)
        {
            int sectorNumber = placeQueue.Dequeue();
            Core.SectorData? sector = db.GetSector(sectorNumber);
            if (sector == null)
                continue;

            (int col, int row) = cellOf[sectorNumber];

            int desiredCol = 1;
            int desiredRow = 0;
            if (parentOf.TryGetValue(sectorNumber, out int parentSector) && cellOf.TryGetValue(parentSector, out var parentCell))
            {
                desiredCol = col - parentCell.Col;
                desiredRow = row - parentCell.Row;
            }

            float directionLength = MathF.Sqrt(desiredCol * desiredCol + desiredRow * desiredRow);
            float unitCol = directionLength > 0 ? desiredCol / directionLength : 1f;
            float unitRow = directionLength > 0 ? desiredRow / directionLength : 0f;

            var orderedOffsets = offsets
                .OrderBy(offset =>
                {
                    float offsetLength = MathF.Sqrt(offset.Item1 * offset.Item1 + offset.Item2 * offset.Item2);
                    float dot = offsetLength > 0
                        ? (offset.Item1 / offsetLength * unitCol) + (offset.Item2 / offsetLength * unitRow)
                        : 0f;
                    return -dot;
                })
                .ToList();

            foreach (ushort warpTarget in sector.Warp.Where(w => w > 0 && visited.ContainsKey(w) && !placed.Contains(w)))
            {
                bool assigned = false;
                for (int ring = 1; ring <= 5 && !assigned; ring++)
                {
                    foreach ((int offsetCol, int offsetRow) in orderedOffsets)
                    {
                        var candidate = (col + offsetCol * ring, row + offsetRow * ring);
                        if (usedCells.Contains(candidate))
                            continue;

                        cellOf[warpTarget] = candidate;
                        usedCells.Add(candidate);
                        placed.Add(warpTarget);
                        placeQueue.Enqueue(warpTarget);
                        assigned = true;
                        break;
                    }
                }
            }
        }

        var positions = new Dictionary<int, SKPoint>();
        foreach ((int sectorNumber, (int col, int row)) in cellOf)
            positions[sectorNumber] = new SKPoint(col * GridX, row * GridY);

        return positions;
    }

    private static (float MinX, float MinY, float MaxX, float MaxY) MeasureBounds(IEnumerable<SKPoint> points)
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (SKPoint point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return (minX - NodeRadius - 10f, minY - NodeRadius - 10f, maxX + NodeRadius + 10f, maxY + NodeRadius + 10f);
    }

    private static void AddLandmark(HashSet<int> landmarks, int sectorNumber)
    {
        if (sectorNumber > 0 && sectorNumber != 65535)
            landmarks.Add(sectorNumber);
    }

    private sealed class MapSnapshot
    {
        public int CurrentSector { get; set; }
        public Dictionary<int, SKPoint> Positions { get; set; } = new();
        public Dictionary<int, Core.SectorData?> Sectors { get; set; } = new();
        public Dictionary<int, int> Depths { get; set; } = new();
        public HashSet<int> Landmarks { get; } = [];
    }

    private sealed class TacticalDrawOp(Rect bounds, TacticalMapControl owner) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
            if (feature == null)
                return;

            using var lease = feature.Lease();
            owner.Draw(lease.SkCanvas, (float)bounds.Width, (float)bounds.Height);
        }
    }
}
