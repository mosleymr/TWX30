using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Core = TWXProxy.Core;

namespace MTC;

public enum TacticalMapViewMode
{
    Bubble,
    Hex,
}

/// <summary>
/// Compact tactical map used by the optional "command deck" skin.
/// It follows the current sector and renders the nearby bubble as either
/// the original node-link bubble view or a warp-count-driven hex view.
/// </summary>
public class TacticalMapControl : Control
{
    private const int MaxDepth = 4;
    private const float BubbleGridX = 110f;
    private const float BubbleGridY = 84f;
    private const float NodeRadius = 20f;
    private const float HexRadius = 34f;
    private const float HexGapFactor = 1.16f;
    private const float DefaultZoomFactor = 0.82f;
    private const float MinZoomFactor = 0.45f;
    private const float MaxZoomFactor = 1.75f;
    private const float ZoomStep = 0.12f;
    private static readonly HexCell[] HexDirections =
    [
        new(1, 0),
        new(1, -1),
        new(0, -1),
        new(-1, 0),
        new(-1, 1),
        new(0, 1),
    ];
    private static readonly SKTypeface PopupTypeface = CreatePopupTypeface();

    private readonly Func<int> _getCurrentSector;
    private readonly Func<Core.ModDatabase?> _getDb;
    private readonly (float X, float Y, float Size, byte Alpha)[] _stars;
    private MapSnapshot _lastSnapshot = new();
    private float _zoomFactor = DefaultZoomFactor;
    private TacticalMapViewMode _viewMode = TacticalMapViewMode.Bubble;
    private float _lastRenderScale;
    private float _lastRenderWidth;
    private float _lastRenderHeight;
    private float _lastWorldCenterX;
    private float _lastWorldCenterY;
    private bool _hasLiveLayout;
    private int _hoveredSectorNumber;
    private string? _hoveredSectorPopupText;
    private Point _hoverPoint;
    private int? _centerSectorOverride;

    public TacticalMapControl(Func<int> getCurrentSector, Func<Core.ModDatabase?> getDb)
    {
        _getCurrentSector = getCurrentSector;
        _getDb = getDb;
        ClipToBounds = true;
        PointerWheelChanged += OnPointerWheelChanged;
        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
        DoubleTapped += OnDoubleTapped;

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

    public int ZoomPercent => (int)Math.Round(_zoomFactor * 100f);
    public TacticalMapViewMode ViewMode => _viewMode;

    public event Action<TacticalMapControl>? ZoomChanged;
    public event Action<TacticalMapControl>? ViewModeChanged;
    public event Action<TacticalMapControl, int>? SectorDoubleClicked;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.Custom(new TacticalDrawOp(bounds, this));
    }

    internal void Draw(SKCanvas canvas, float width, float height)
    {
        DrawBackdrop(canvas, width, height);

        MapSnapshot snapshot = BuildSnapshot();
        _lastSnapshot = snapshot;
        if (snapshot.Positions.Count == 0)
        {
            _hasLiveLayout = false;
            _hoveredSectorNumber = 0;
            _hoveredSectorPopupText = null;
            DrawEmptyState(canvas, width, height);
            return;
        }

        float contentMargin = _viewMode == TacticalMapViewMode.Hex ? HexRadius + 18f : NodeRadius + 12f;
        (float minX, float minY, float maxX, float maxY) = MeasureBounds(snapshot.Positions.Values, contentMargin);
        float contentWidth = Math.Max(1f, maxX - minX);
        float contentHeight = Math.Max(1f, maxY - minY);
        float framePadding = snapshot.Positions.Count > 1
            ? (_viewMode == TacticalMapViewMode.Hex ? 156f : 128f)
            : (_viewMode == TacticalMapViewMode.Hex ? 112f : 92f);
        float scale = Math.Min((width - framePadding) / contentWidth, (height - framePadding) / contentHeight);
        scale = Math.Clamp(
            scale * _zoomFactor,
            _viewMode == TacticalMapViewMode.Hex ? 0.26f : 0.34f,
            _viewMode == TacticalMapViewMode.Hex ? 1.08f : 1.12f);

        _lastRenderScale = scale;
        _lastRenderWidth = width;
        _lastRenderHeight = height;
        _lastWorldCenterX = (minX + maxX) / 2f;
        _lastWorldCenterY = (minY + maxY) / 2f;
        _hasLiveLayout = true;

        canvas.Save();
        canvas.Translate(width / 2f, height / 2f);
        canvas.Scale(scale);
        canvas.Translate(-_lastWorldCenterX, -_lastWorldCenterY);

        if (_viewMode == TacticalMapViewMode.Hex)
            DrawHexCells(canvas, snapshot);
        else
        {
            DrawEdges(canvas, snapshot);
            DrawNodes(canvas, snapshot);
        }

        canvas.Restore();
        RefreshHoverPopup();
        DrawOverlayLegend(canvas, width, height, snapshot, _viewMode);
        DrawSectorHoverPopup(canvas, width, height);
    }

    public void AdjustZoom(float delta)
    {
        SetZoom(_zoomFactor + delta);
    }

    public void ResetZoom()
    {
        SetZoom(DefaultZoomFactor);
    }

    public void SetViewMode(TacticalMapViewMode viewMode)
    {
        if (_viewMode == viewMode)
            return;

        _viewMode = viewMode;
        InvalidateVisual();
        ViewModeChanged?.Invoke(this);
    }

    public void CenterOnSector(int sectorNumber)
    {
        if (sectorNumber <= 0)
            return;

        _centerSectorOverride = sectorNumber;
        InvalidateVisual();
    }

    public void FollowLiveSector()
    {
        if (_centerSectorOverride == null)
            return;

        _centerSectorOverride = null;
        InvalidateVisual();
    }

    private void SetZoom(float zoomFactor)
    {
        float clamped = Math.Clamp(zoomFactor, MinZoomFactor, MaxZoomFactor);
        if (Math.Abs(clamped - _zoomFactor) < 0.001f)
            return;

        _zoomFactor = clamped;
        InvalidateVisual();
        ZoomChanged?.Invoke(this);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y > 0)
            AdjustZoom(ZoomStep);
        else if (e.Delta.Y < 0)
            AdjustZoom(-ZoomStep);
        else
            return;

        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        Point pointerPosition = e.GetPosition(this);
        bool changed = UpdateHoveredSector(pointerPosition);
        if (changed)
            InvalidateVisual();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (_hoveredSectorNumber == 0 && _hoveredSectorPopupText == null)
            return;

        _hoveredSectorNumber = 0;
        _hoveredSectorPopupText = null;
        InvalidateVisual();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        int sectorNumber = HitTestSector(e.GetPosition(this));
        if (sectorNumber <= 0)
            return;

        SectorDoubleClicked?.Invoke(this, sectorNumber);
        e.Handled = true;
    }

    private bool UpdateHoveredSector(Point pointerPosition)
    {
        int hoveredSector = HitTestSector(pointerPosition);
        bool sectorChanged = hoveredSector != _hoveredSectorNumber;
        bool pointChanged = !_hoverPoint.Equals(pointerPosition);
        _hoverPoint = pointerPosition;

        if (!sectorChanged && !pointChanged)
            return false;

        _hoveredSectorNumber = hoveredSector;
        _hoveredSectorPopupText = hoveredSector > 0 ? BuildSectorPopupText(hoveredSector) : null;
        return true;
    }

    private int HitTestSector(Point pointerPosition)
    {
        if (!_hasLiveLayout || _lastRenderScale <= 0f || _lastSnapshot.Positions.Count == 0)
            return 0;

        SKPoint worldPoint = ScreenToWorld(pointerPosition);
        if (_viewMode == TacticalMapViewMode.Hex)
        {
            foreach ((int sectorNumber, SKPoint center) in _lastSnapshot.Positions)
            {
                if (IsPointInHex(worldPoint, center, HexRadius))
                    return sectorNumber;
            }

            return 0;
        }

        int closestSector = 0;
        float closestDistance = float.MaxValue;
        foreach ((int sectorNumber, SKPoint center) in _lastSnapshot.Positions)
        {
            float distance = Dist(center, worldPoint);
            if (distance > NodeRadius + 10f || distance >= closestDistance)
                continue;

            closestDistance = distance;
            closestSector = sectorNumber;
        }

        return closestSector;
    }

    private SKPoint ScreenToWorld(Point pointerPosition)
    {
        return new SKPoint(
            ((float)pointerPosition.X - (_lastRenderWidth / 2f)) / _lastRenderScale + _lastWorldCenterX,
            ((float)pointerPosition.Y - (_lastRenderHeight / 2f)) / _lastRenderScale + _lastWorldCenterY);
    }

    private void RefreshHoverPopup()
    {
        if (_hoveredSectorNumber <= 0)
            return;

        if (!_lastSnapshot.Positions.ContainsKey(_hoveredSectorNumber))
        {
            _hoveredSectorNumber = 0;
            _hoveredSectorPopupText = null;
            return;
        }

        _hoveredSectorPopupText = BuildSectorPopupText(_hoveredSectorNumber);
    }

    private string BuildSectorPopupText(int sectorNumber)
    {
        Core.ModDatabase? db = _getDb();
        Core.SectorData? sector = db?.GetSector(sectorNumber);
        if (sector == null)
            _lastSnapshot.Sectors.TryGetValue(sectorNumber, out sector);

        return SectorScanFormatter.FormatSectorTooltip(sectorNumber, sector, db);
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

    private static void DrawHexCells(SKCanvas canvas, MapSnapshot snapshot)
    {
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var edgeGlow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7f };
        using var edgePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f };
        using var sectorPaint = new SKPaint
        {
            IsAntialias = true,
            TextSize = 13f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.Default,
        };
        using var portPaint = new SKPaint
        {
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.Default,
        };

        foreach ((int sectorNumber, SKPoint position) in snapshot.Positions.OrderBy(kvp => snapshot.Depths.GetValueOrDefault(kvp.Key)))
        {
            snapshot.Sectors.TryGetValue(sectorNumber, out Core.SectorData? sector);
            var palette = GetHexPalette(snapshot, sectorNumber, sector);
            using SKPath hexPath = CreateHexPath(position, HexRadius);

            fillPaint.Color = palette.Fill;
            canvas.DrawPath(hexPath, fillPaint);

            edgeGlow.Color = palette.Edge.WithAlpha((byte)(sectorNumber == snapshot.CurrentSector ? 72 : 34));
            edgePaint.Color = palette.Edge;
            canvas.DrawPath(hexPath, edgeGlow);
            canvas.DrawPath(hexPath, edgePaint);

            string portLabel = GetPortTypeLabel(sector);
            sectorPaint.Color = palette.Text;
            portPaint.Color = palette.PortText;
            float sectorY = string.IsNullOrEmpty(portLabel) ? position.Y + 4f : position.Y - 4f;
            canvas.DrawText(sectorNumber.ToString(), position.X, sectorY, sectorPaint);
            if (!string.IsNullOrEmpty(portLabel))
                canvas.DrawText(portLabel, position.X, position.Y + 14f, portPaint);
        }
    }

    private void DrawSectorHoverPopup(SKCanvas canvas, float width, float height)
    {
        if (_hoveredSectorNumber <= 0 || string.IsNullOrWhiteSpace(_hoveredSectorPopupText))
            return;

        string[] lines = _hoveredSectorPopupText
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return;

        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x00, 0x00, 0x00, 96),
            ImageFilter = SKImageFilter.CreateBlur(10f, 10f),
        };
        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x05, 0x0f, 0x14, 242),
            Style = SKPaintStyle.Fill,
        };
        using var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x18, 0xd6, 0xd1, 220),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
        };
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0xe2, 0xfb, 0xff),
            TextSize = 13f,
            Typeface = PopupTypeface,
        };

        SKFontMetrics metrics = textPaint.FontMetrics;
        float lineHeight = metrics.Descent - metrics.Ascent + 2f;
        float textWidth = lines.Max(textPaint.MeasureText);
        float popupWidth = MathF.Min(width - 16f, textWidth + 24f);
        float popupHeight = MathF.Min(height - 16f, (lines.Length * lineHeight) + 18f);

        float x = (float)_hoverPoint.X + 18f;
        float y = (float)_hoverPoint.Y + 18f;
        if (x + popupWidth > width - 8f)
            x = Math.Max(8f, (float)_hoverPoint.X - popupWidth - 18f);
        if (y + popupHeight > height - 8f)
            y = Math.Max(8f, height - popupHeight - 8f);

        var popupRect = new SKRect(x, y, x + popupWidth, y + popupHeight);
        canvas.DrawRoundRect(new SKRoundRect(popupRect, 10f, 10f), shadowPaint);
        canvas.DrawRoundRect(new SKRoundRect(popupRect, 10f, 10f), fillPaint);
        canvas.DrawRoundRect(new SKRoundRect(popupRect, 10f, 10f), borderPaint);

        float baseline = y + 10f - metrics.Ascent;
        float maxBaseline = y + popupHeight - 10f;
        foreach (string line in lines)
        {
            if (baseline > maxBaseline)
                break;

            canvas.DrawText(line, x + 12f, baseline, textPaint);
            baseline += lineHeight;
        }
    }

    private static void DrawOverlayLegend(SKCanvas canvas, float width, float height, MapSnapshot snapshot, TacticalMapViewMode viewMode)
    {
        string text = snapshot.CenterSector > 0
            ? snapshot.CenterSector != snapshot.CurrentSector && snapshot.CurrentSector > 0
                ? $"CENTER {snapshot.CenterSector}  |  LIVE {snapshot.CurrentSector}  |  {viewMode.ToString().ToUpperInvariant()} VIEW  |  {snapshot.Positions.Count} SECTORS"
                : $"LIVE SECTOR {snapshot.CenterSector}  |  {viewMode.ToString().ToUpperInvariant()} VIEW  |  {snapshot.Positions.Count} SECTORS"
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

        canvas.DrawRoundRect(new SKRoundRect(new SKRect(16, height - 36, 326, height - 12), 8, 8), overlayPaint);
        canvas.DrawText(text, 28, height - 18, textPaint);
    }

    private MapSnapshot BuildSnapshot()
    {
        var snapshot = new MapSnapshot();
        Core.ModDatabase? db = _getDb();
        int liveSector = Math.Max(1, _getCurrentSector());
        int centerSector = _centerSectorOverride.GetValueOrDefault(liveSector);
        snapshot.CurrentSector = liveSector;
        snapshot.CenterSector = centerSector;

        if (db == null || centerSector <= 0)
            return snapshot;

        var visited = new Dictionary<int, int> { [centerSector] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(centerSector);

        while (queue.Count > 0)
        {
            int sectorNumber = queue.Dequeue();
            int depth = visited[sectorNumber];
            Core.SectorData? sector = db.GetSector(sectorNumber);
            snapshot.Sectors[sectorNumber] = sector;

            if (sector == null || depth >= MaxDepth)
                continue;

            foreach (int linkedSector in EnumerateLinkedSectors(sector))
            {
                if (visited.ContainsKey(linkedSector))
                    continue;

                visited[linkedSector] = depth + 1;
                queue.Enqueue(linkedSector);
            }
        }

        foreach (int sectorNumber in visited.Keys.Where(sectorNumber => !snapshot.Sectors.ContainsKey(sectorNumber)))
            snapshot.Sectors[sectorNumber] = db.GetSector(sectorNumber);

        snapshot.Depths = visited;
        if (_viewMode == TacticalMapViewMode.Hex)
            ApplyHexLayout(snapshot, db, centerSector, visited);
        else
            snapshot.Positions = ComputeBubblePositions(db, centerSector, visited);

        var header = db.DBHeader;
        AddLandmark(snapshot.Landmarks, header.StarDock);
        AddLandmark(snapshot.Landmarks, header.Rylos);
        AddLandmark(snapshot.Landmarks, header.AlphaCentauri);

        return snapshot;
    }

    private static Dictionary<int, SKPoint> ComputeBubblePositions(Core.ModDatabase db, int centerSector, Dictionary<int, int> visited)
    {
        var cellOf = new Dictionary<int, (int Col, int Row)> { [centerSector] = (0, 0) };
        var usedCells = new HashSet<(int Col, int Row)> { (0, 0) };
        var placed = new HashSet<int> { centerSector };
        var placeQueue = new Queue<int>();
        placeQueue.Enqueue(centerSector);
        Dictionary<int, int> parentOf = BuildParentMap(db, centerSector, visited);

        (int Col, int Row)[] offsets =
        [
            ( 1,  0), ( 1,  1), ( 0,  1), (-1,  1),
            (-1,  0), (-1, -1), ( 0, -1), ( 1, -1),
        ];

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
            positions[sectorNumber] = new SKPoint(col * BubbleGridX, row * BubbleGridY);

        return positions;
    }

    private static void ApplyHexLayout(MapSnapshot snapshot, Core.ModDatabase db, int centerSector, Dictionary<int, int> visited)
    {
        var cellOf = new Dictionary<int, HexCell> { [centerSector] = new HexCell(0, 0) };
        var sectorByCell = new Dictionary<HexCell, int> { [new HexCell(0, 0)] = centerSector };
        var placed = new HashSet<int> { centerSector };
        var usedCells = new HashSet<HexCell> { new(0, 0) };
        var placeQueue = new Queue<int>();
        placeQueue.Enqueue(centerSector);
        Dictionary<int, int> parentOf = BuildParentMap(db, centerSector, visited);

        while (placeQueue.Count > 0)
        {
            int sectorNumber = placeQueue.Dequeue();
            Core.SectorData? sector = db.GetSector(sectorNumber);
            if (sector == null)
                continue;

            HexCell origin = cellOf[sectorNumber];
            List<int> orderedDirections = OrderHexDirections(origin, sectorNumber, cellOf, parentOf);

            foreach (int linkedSector in EnumerateLinkedSectors(sector).Where(linkedSector => visited.ContainsKey(linkedSector) && !placed.Contains(linkedSector)))
            {
                if (!TryAssignHexCell(db, linkedSector, origin, orderedDirections, usedCells, sectorByCell, out HexCell assignedCell))
                    continue;

                cellOf[linkedSector] = assignedCell;
                sectorByCell[assignedCell] = linkedSector;
                placed.Add(linkedSector);
                placeQueue.Enqueue(linkedSector);
            }
        }

        snapshot.HexCells = cellOf;
        snapshot.SectorByHexCell = sectorByCell;
        snapshot.Positions = cellOf.ToDictionary(kvp => kvp.Key, kvp => HexCellToPoint(kvp.Value));
    }

    private static Dictionary<int, int> BuildParentMap(Core.ModDatabase db, int centerSector, Dictionary<int, int> visited)
    {
        var parentOf = new Dictionary<int, int>();
        var bfs = new Queue<int>();
        var seen = new HashSet<int> { centerSector };
        bfs.Enqueue(centerSector);

        while (bfs.Count > 0)
        {
            int sectorNumber = bfs.Dequeue();
            Core.SectorData? sector = db.GetSector(sectorNumber);
            if (sector == null)
                continue;

            foreach (int linkedSector in EnumerateLinkedSectors(sector).Where(linkedSector => visited.ContainsKey(linkedSector) && !seen.Contains(linkedSector)))
            {
                seen.Add(linkedSector);
                parentOf[linkedSector] = sectorNumber;
                bfs.Enqueue(linkedSector);
            }
        }

        return parentOf;
    }

    private static List<int> OrderHexDirections(HexCell origin, int sectorNumber, Dictionary<int, HexCell> cellOf, Dictionary<int, int> parentOf)
    {
        HexCell desiredDirection = new(1, 0);
        if (parentOf.TryGetValue(sectorNumber, out int parentSector) && cellOf.TryGetValue(parentSector, out HexCell parentCell))
            desiredDirection = new(origin.Q - parentCell.Q, origin.R - parentCell.R);

        return Enumerable.Range(0, HexDirections.Length)
            .OrderByDescending(index => HexDirections[index].Q * desiredDirection.Q + HexDirections[index].R * desiredDirection.R)
            .ThenBy(index => index)
            .ToList();
    }

    private static bool TryAssignHexCell(
        Core.ModDatabase db,
        int sectorNumber,
        HexCell origin,
        IReadOnlyList<int> orderedDirections,
        HashSet<HexCell> usedCells,
        IReadOnlyDictionary<HexCell, int> sectorByCell,
        out HexCell assignedCell)
    {
        HexCell bestConnectedCandidate = default;
        int bestConnectedRing = int.MaxValue;
        int bestConnectedDistance = int.MaxValue;

        HexCell bestIsolatedCandidate = default;
        int bestIsolatedRing = int.MaxValue;
        int bestIsolatedDistance = int.MaxValue;

        for (int ring = 1; ring <= 5; ring++)
        {
            foreach (int directionIndex in orderedDirections)
            {
                HexCell direction = HexDirections[directionIndex];
                HexCell candidate = new(origin.Q + direction.Q * ring, origin.R + direction.R * ring);
                if (usedCells.Contains(candidate))
                    continue;

                List<int> occupiedNeighbors = GetAdjacentOccupiedSectors(candidate, sectorByCell);
                bool touchesConnectedSector = false;
                bool touchesUnrelatedSector = false;
                foreach (int neighborSector in occupiedNeighbors)
                {
                    if (!AreSectorsConnected(db, sectorNumber, neighborSector))
                    {
                        touchesUnrelatedSector = true;
                        break;
                    }

                    touchesConnectedSector = true;
                }

                if (touchesUnrelatedSector)
                    continue;

                int originDistance = HexDistance(origin, candidate);
                if (touchesConnectedSector)
                {
                    if (ring < bestConnectedRing || (ring == bestConnectedRing && originDistance < bestConnectedDistance))
                    {
                        bestConnectedCandidate = candidate;
                        bestConnectedRing = ring;
                        bestConnectedDistance = originDistance;
                    }

                    continue;
                }

                if (occupiedNeighbors.Count == 0 && (ring < bestIsolatedRing || (ring == bestIsolatedRing && originDistance < bestIsolatedDistance)))
                {
                    bestIsolatedCandidate = candidate;
                    bestIsolatedRing = ring;
                    bestIsolatedDistance = originDistance;
                }
            }
        }

        if (bestConnectedRing != int.MaxValue)
        {
            usedCells.Add(bestConnectedCandidate);
            assignedCell = bestConnectedCandidate;
            return true;
        }

        if (bestIsolatedRing != int.MaxValue)
        {
            usedCells.Add(bestIsolatedCandidate);
            assignedCell = bestIsolatedCandidate;
            return true;
        }

        assignedCell = default;
        return false;
    }

    private static IEnumerable<int> EnumerateLinkedSectors(Core.SectorData sector)
    {
        return sector.Warp
            .Where(w => w > 0)
            .Select(w => (int)w)
            .Concat(sector.WarpsIn.Where(w => w > 0).Select(w => (int)w))
            .Distinct();
    }

    private static List<int> GetAdjacentOccupiedSectors(HexCell candidate, IReadOnlyDictionary<HexCell, int> sectorByCell)
    {
        var neighbors = new List<int>(HexDirections.Length);
        foreach (HexCell direction in HexDirections)
        {
            HexCell adjacent = new(candidate.Q + direction.Q, candidate.R + direction.R);
            if (sectorByCell.TryGetValue(adjacent, out int sectorNumber))
                neighbors.Add(sectorNumber);
        }

        return neighbors;
    }

    private static bool AreSectorsConnected(Core.ModDatabase db, int firstSector, int secondSector)
    {
        if (firstSector == secondSector)
            return true;

        Core.SectorData? first = db.GetSector(firstSector);
        Core.SectorData? second = db.GetSector(secondSector);
        if (first == null || second == null)
            return false;

        return first.Warp.Contains((ushort)secondSector)
            || first.WarpsIn.Contains((ushort)secondSector)
            || second.Warp.Contains((ushort)firstSector)
            || second.WarpsIn.Contains((ushort)firstSector);
    }

    private static int HexDistance(HexCell a, HexCell b)
    {
        int dq = a.Q - b.Q;
        int dr = a.R - b.R;
        int ds = (a.Q + a.R) - (b.Q + b.R);
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(ds)) / 2;
    }

    private static (float MinX, float MinY, float MaxX, float MaxY) MeasureBounds(IEnumerable<SKPoint> points, float margin)
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

        return (minX - margin, minY - margin, maxX + margin, maxY + margin);
    }

    private static void AddLandmark(HashSet<int> landmarks, int sectorNumber)
    {
        if (sectorNumber > 0 && sectorNumber != 65535)
            landmarks.Add(sectorNumber);
    }

    private static SKPoint HexCellToPoint(HexCell cell)
    {
        float stepX = HexRadius * 1.7320508f * HexGapFactor;
        float stepY = HexRadius * 1.5f * HexGapFactor;
        return new SKPoint(
            stepX * (cell.Q + cell.R * 0.5f),
            stepY * cell.R);
    }

    private static SKPath CreateHexPath(SKPoint center, float radius)
    {
        SKPoint[] vertices = GetHexVertices(center, radius);
        var path = new SKPath();
        path.MoveTo(vertices[0]);
        for (int index = 1; index < vertices.Length; index++)
            path.LineTo(vertices[index]);
        path.Close();
        return path;
    }

    private static SKPoint[] GetHexVertices(SKPoint center, float radius)
    {
        var vertices = new SKPoint[6];
        for (int index = 0; index < 6; index++)
        {
            float angleDegrees = -90f + (60f * index);
            float angle = angleDegrees * MathF.PI / 180f;
            vertices[index] = new SKPoint(
                center.X + MathF.Cos(angle) * radius,
                center.Y + MathF.Sin(angle) * radius);
        }

        return vertices;
    }

    private static bool IsPointInHex(SKPoint point, SKPoint center, float radius)
    {
        SKPoint[] vertices = GetHexVertices(center, radius);
        bool inside = false;
        for (int index = 0, previous = vertices.Length - 1; index < vertices.Length; previous = index++)
        {
            SKPoint current = vertices[index];
            SKPoint prior = vertices[previous];
            bool intersects = ((current.Y > point.Y) != (prior.Y > point.Y)) &&
                              (point.X < ((prior.X - current.X) * (point.Y - current.Y) / ((prior.Y - current.Y) + float.Epsilon)) + current.X);
            if (intersects)
                inside = !inside;
        }

        return inside;
    }

    private static float Dist(SKPoint a, SKPoint b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static SKTypeface CreatePopupTypeface()
    {
        if (OperatingSystem.IsMacOS())
            return SKTypeface.FromFamilyName("Menlo");
        if (OperatingSystem.IsWindows())
            return SKTypeface.FromFamilyName("Consolas");
        return SKTypeface.FromFamilyName("DejaVu Sans Mono");
    }

    private static (SKColor Fill, SKColor Edge, SKColor Text, SKColor PortText) GetHexPalette(MapSnapshot snapshot, int sectorNumber, Core.SectorData? sector)
    {
        bool isCurrent = sectorNumber == snapshot.CurrentSector;
        bool isLandmark = snapshot.Landmarks.Contains(sectorNumber);
        if (isCurrent)
        {
            return (
                new SKColor(0x5b, 0x3c, 0x12, 210),
                new SKColor(0xff, 0xc0, 0x54),
                new SKColor(0xff, 0xf2, 0xc7),
                new SKColor(0xff, 0xda, 0x7c));
        }

        if (isLandmark)
        {
            return (
                new SKColor(0x14, 0x28, 0x34, 190),
                new SKColor(0x58, 0xc1, 0xf7),
                new SKColor(0xe8, 0xf7, 0xff),
                new SKColor(0x9f, 0xdd, 0xff));
        }

        return sector?.Explored switch
        {
            Core.ExploreType.Yes => (
                new SKColor(0x0e, 0x1c, 0x14, 190),
                new SKColor(0x19, 0xcc, 0x3b),
                new SKColor(0xdb, 0xff, 0xe2),
                new SKColor(0x8a, 0xf6, 0xa1)),
            Core.ExploreType.Density => (
                new SKColor(0x12, 0x1d, 0x22, 190),
                new SKColor(0x2c, 0xc7, 0xd9),
                new SKColor(0xd9, 0xf9, 0xff),
                new SKColor(0x8f, 0xeb, 0xff)),
            _ => (
                new SKColor(0x18, 0x0e, 0x20, 190),
                new SKColor(0xb2, 0x29, 0xcf),
                new SKColor(0xf4, 0xe8, 0xfa),
                new SKColor(0xd7, 0x92, 0xe6)),
        };
    }

    private static string GetPortTypeLabel(Core.SectorData? sector)
    {
        if (sector?.SectorPort == null || sector.SectorPort.Dead)
            return string.Empty;

        Core.Port port = sector.SectorPort;
        if (port.ClassIndex == 9)
            return "SD";

        if (port.ClassIndex == 0 && !string.IsNullOrWhiteSpace(port.Name))
        {
            if (string.Equals(port.Name, "Rylos", StringComparison.OrdinalIgnoreCase))
                return "RY";
            if (string.Equals(port.Name, "Alpha Centauri", StringComparison.OrdinalIgnoreCase))
                return "AC";
            if (string.Equals(port.Name, "Sol", StringComparison.OrdinalIgnoreCase))
                return "SOL";
        }

        bool hasFuel = port.BuyProduct.TryGetValue(Core.ProductType.FuelOre, out bool buyFuel);
        bool hasOrg = port.BuyProduct.TryGetValue(Core.ProductType.Organics, out bool buyOrg);
        bool hasEquip = port.BuyProduct.TryGetValue(Core.ProductType.Equipment, out bool buyEquip);
        if (hasFuel || hasOrg || hasEquip)
        {
            char fuel = hasFuel && buyFuel ? 'B' : 'S';
            char org = hasOrg && buyOrg ? 'B' : 'S';
            char equip = hasEquip && buyEquip ? 'B' : 'S';
            return new string([fuel, org, equip]);
        }

        return port.ClassIndex > 0 ? $"C{port.ClassIndex}" : "PORT";
    }

    private readonly record struct HexCell(int Q, int R);

    private sealed class MapSnapshot
    {
        public int CurrentSector { get; set; }
        public int CenterSector { get; set; }
        public Dictionary<int, SKPoint> Positions { get; set; } = new();
        public Dictionary<int, Core.SectorData?> Sectors { get; set; } = new();
        public Dictionary<int, int> Depths { get; set; } = new();
        public Dictionary<int, HexCell> HexCells { get; set; } = new();
        public Dictionary<HexCell, int> SectorByHexCell { get; set; } = new();
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
