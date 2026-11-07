using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClipwellWin.Services;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ClipwellWin.Views;

/// <summary>
/// Draws the knowledge network: topic-colored nodes (circle = clipboard, diamond = voice note, square = recording),
/// typed links, topic labels. Mouse wheel zooms at the cursor, dragging the background pans, dragging a node moves it.
/// </summary>
public sealed class BrainCanvas : FrameworkElement
{
    // The first (largest) topic uses the app accent; the others avoid hues too close to it.
    public static readonly Color[] Palette =
    [
        Color.FromRgb(0xE8, 0x88, 0x32), Color.FromRgb(0x4F, 0xC3, 0xF7), Color.FromRgb(0xF0, 0x62, 0x92),
        Color.FromRgb(0xAE, 0xD5, 0x81), Color.FromRgb(0xBA, 0x68, 0xC8), Color.FromRgb(0x4D, 0xB6, 0xAC),
        Color.FromRgb(0xFF, 0xD5, 0x4F), Color.FromRgb(0x95, 0x75, 0xCD), Color.FromRgb(0xDC, 0xE7, 0x75),
        Color.FromRgb(0x64, 0xB5, 0xF6), Color.FromRgb(0xE5, 0x73, 0x73), Color.FromRgb(0x81, 0xC7, 0x84),
    ];

    private static readonly Color Unclustered = Color.FromRgb(0x7A, 0x7A, 0x7A);
    // Selection is white: the accent already marks the main topic.
    private static readonly Color Accent = Colors.White;
    private static readonly Color Background = Color.FromRgb(0x0A, 0x0A, 0x0A);
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface NodeFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly Dictionary<(Color, byte), SolidColorBrush> _brushes = [];
    private static readonly Pen SelectedOutline = FrozenPen(Accent, 2.5);
    private static readonly Pen HoverOutline = FrozenPen(Color.FromRgb(0xD8, 0xD2, 0xCA), 1.5);
    private static readonly Pen NodeOutline = FrozenPen(Background, 1);
    private BrainGraph? _graph;
    private double[] _x = [];
    private double[] _y = [];
    private double _zoom = 1;
    private Vector _offset;
    private bool _fitted;
    private Point _dragStart;
    private Vector _offsetAtDragStart;
    private int _dragNode = -1;
    private bool _panning;
    private bool _moved;

    private static Pen FrozenPen(Color color, double width)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, width);
        pen.Freeze();
        return pen;
    }

    public BrainCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Arrow;
    }

    /// <summary>Raised with the clicked node index, or -1 for a click on empty space.</summary>
    public event Action<int>? NodeClicked;

    public event Action<int>? NodeDoubleClicked;

    public BrainGraph? Graph => _graph;
    public int SelectedNode { get; private set; } = -1;
    public int HoverNode { get; private set; } = -1;
    public bool AllowNodeDrag { get; set; } = true;

    /// <summary>Nodes drawn at full strength; everything else is dimmed. Null shows all.</summary>
    public IReadOnlySet<int>? Highlight { get; private set; }

    public HashSet<GraphEdgeKind> VisibleEdgeKinds { get; } = [GraphEdgeKind.Semantic, GraphEdgeKind.Time, GraphEdgeKind.Session, GraphEdgeKind.Domain, GraphEdgeKind.Summary, GraphEdgeKind.Manual, GraphEdgeKind.Category];

    public static Color ClusterColor(int cluster) => cluster < 0 ? Unclustered : Palette[cluster % Palette.Length];

    public void SetGraph(BrainGraph graph, double[] x, double[] y, bool keepView)
    {
        _graph = graph;
        _x = (double[])x.Clone();
        _y = (double[])y.Clone();
        SelectedNode = -1;
        HoverNode = -1;
        Highlight = null;
        if (!keepView)
        {
            _fitted = false;
            UserMovedView = false;
        }
        InvalidateVisual();
    }

    /// <summary>Set once the user zooms, pans or jumps to a node; the animated layout then stops re-fitting.</summary>
    public bool UserMovedView { get; private set; }

    public void UpdatePositions(double[] x, double[] y)
    {
        if (_graph == null || x.Length != _graph.Nodes.Count) return;
        _x = (double[])x.Clone();
        _y = (double[])y.Clone();
        InvalidateVisual();
    }

    public (double[] X, double[] Y) Positions() => (_x, _y);

    public void Select(int node)
    {
        SelectedNode = node;
        InvalidateVisual();
    }

    public void SetHighlight(IReadOnlySet<int>? nodes)
    {
        Highlight = nodes;
        InvalidateVisual();
    }

    public void FitToView()
    {
        UserMovedView = false;
        _fitted = false;
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (UserMovedView)
            _offset += new Vector((sizeInfo.NewSize.Width - sizeInfo.PreviousSize.Width) / 2,
                (sizeInfo.NewSize.Height - sizeInfo.PreviousSize.Height) / 2);
        else _fitted = false;
        InvalidateVisual();
    }

    public void CenterOn(IEnumerable<int> nodes)
    {
        var list = nodes.Where(i => i >= 0 && i < _x.Length).ToList();
        if (list.Count == 0 || ActualWidth <= 0) return;
        UserMovedView = true;
        var cx = list.Average(i => _x[i]);
        var cy = list.Average(i => _y[i]);
        if (list.Count > 1)
        {
            var w = list.Max(i => _x[i]) - list.Min(i => _x[i]) + 120;
            var h = list.Max(i => _y[i]) - list.Min(i => _y[i]) + 120;
            _zoom = Math.Clamp(Math.Min(ActualWidth / w, ActualHeight / h), 0.15, 2.5);
        }
        else
        {
            _zoom = Math.Max(_zoom, 1.2);
        }
        _offset = new Vector(ActualWidth / 2 - cx * _zoom, ActualHeight / 2 - cy * _zoom);
        InvalidateVisual();
    }

    // ---- Coordinates -----------------------------------------------------------------------------------------

    private Point ToScreen(int i) => new(_x[i] * _zoom + _offset.X, _y[i] * _zoom + _offset.Y);

    private void Fit()
    {
        if (_graph == null || _graph.Nodes.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var minX = _x.Min() - 40;
        var maxX = _x.Max() + 40;
        var minY = _y.Min() - 40;
        var maxY = _y.Max() + 40;
        _zoom = Math.Clamp(Math.Min(ActualWidth / Math.Max(1, maxX - minX), ActualHeight / Math.Max(1, maxY - minY)), 0.05, 2.5);
        _offset = new Vector(ActualWidth / 2 - (minX + maxX) / 2 * _zoom, ActualHeight / 2 - (minY + maxY) / 2 * _zoom);
        _fitted = true;
    }

    private double Radius(int i)
    {
        var degree = _graph!.Nodes[i].Degree;
        return Math.Min(15, 4 + 1.6 * Math.Sqrt(degree)) * Math.Clamp(Math.Sqrt(_zoom), 0.6, 1.6);
    }

    private int NodeAt(Point p)
    {
        if (_graph == null) return -1;
        var best = -1;
        var bestDist = double.MaxValue;
        for (var i = 0; i < _graph.Nodes.Count; i++)
        {
            var s = ToScreen(i);
            var d = (s - p).Length;
            var r = Radius(i) + 4;
            if (d <= r && d < bestDist) { best = i; bestDist = d; }
        }
        return best;
    }

    // ---- Rendering -------------------------------------------------------------------------------------------

    private SolidColorBrush BrushOf(Color c, byte alpha)
    {
        if (_brushes.TryGetValue((c, alpha), out var b)) return b;
        b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        _brushes[(c, alpha)] = b;
        return b;
    }

    private bool IsLit(int i) => Highlight == null || Highlight.Contains(i);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(BrushOf(Background, 255), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_graph == null || _graph.Nodes.Count == 0) return;
        if (!_fitted) Fit();

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        DrawEdges(dc);
        DrawNodes(dc);
        if (SelectedNode < 0) DrawClusterLabels(dc, dpi);
        DrawNodeLabels(dc, dpi);
    }

    private void DrawEdges(DrawingContext dc)
    {
        var selectedEdges = SelectedNode >= 0 ? _graph!.EdgesOf(SelectedNode).ToHashSet() : null;
        var buckets = new Dictionary<(Color Color, byte Alpha, double Width), StreamGeometry>();
        var contexts = new Dictionary<(Color, byte, double), StreamGeometryContext>();
        var view = new Rect(-50, -50, ActualWidth + 100, ActualHeight + 100);

        foreach (var e in _graph!.Edges)
        {
            if (!VisibleEdgeKinds.Contains(e.Kind)) continue;
            var a = ToScreen(e.A);
            var b = ToScreen(e.B);
            if (!view.Contains(a) && !view.Contains(b)) continue;

            var lit = IsLit(e.A) && IsLit(e.B);
            var selected = selectedEdges?.Contains(e) == true;
            var strength = BrainGraph.Strength(e);
            Color color = e.Kind switch
            {
                GraphEdgeKind.Semantic => _graph.Nodes[e.A].Cluster == _graph.Nodes[e.B].Cluster && _graph.Nodes[e.A].Cluster >= 0
                    ? ClusterColor(_graph.Nodes[e.A].Cluster) : Color.FromRgb(0xB0, 0xB0, 0xB0),
                GraphEdgeKind.Time => Color.FromRgb(0x64, 0xB5, 0xF6),
                GraphEdgeKind.Session => Color.FromRgb(0xE0, 0xE0, 0xE0),
                GraphEdgeKind.Summary => Color.FromRgb(0xFF, 0xB7, 0x4D),
                GraphEdgeKind.Manual => Color.FromRgb(0xBA, 0xA0, 0xFF),
                GraphEdgeKind.Category => _graph.Nodes[e.A].Cluster == _graph.Nodes[e.B].Cluster && _graph.Nodes[e.A].Cluster >= 0
                    ? ClusterColor(_graph.Nodes[e.A].Cluster) : Color.FromRgb(0x4D, 0xD0, 0xE1),
                _ => Color.FromRgb(0x81, 0xC7, 0x84),
            };
            if (selected) color = Accent;
            byte alpha = selected ? (byte)230 : !lit ? (byte)14 : (byte)(40 + 120 * Math.Clamp((strength - 0.7) / 0.3, 0, 1));
            alpha = (byte)(alpha / 16 * 16);
            var width = selected ? 2.2 : e.Kind == GraphEdgeKind.Semantic ? 1.3 : 1.0;
            var key = (color, alpha, width);
            if (!contexts.TryGetValue(key, out var ctx))
            {
                var geometry = new StreamGeometry();
                buckets[key] = geometry;
                ctx = geometry.Open();
                contexts[key] = ctx;
            }
            ctx.BeginFigure(a, false, false);
            ctx.LineTo(b, true, false);
            if (e.Directed && (b - a).Length > 24)
            {
                var direction = b - a;
                direction.Normalize();
                var tip = a + (b - a) * 0.72;
                var side = new Vector(-direction.Y, direction.X) * 3.5;
                ctx.BeginFigure(tip - direction * 7 + side, false, false);
                ctx.LineTo(tip, true, false);
                ctx.LineTo(tip - direction * 7 - side, true, false);
            }
        }

        foreach (var (key, ctx) in contexts)
        {
            ctx.Close();
            var geometry = buckets[key];
            geometry.Freeze();
            var pen = new Pen(BrushOf(key.Item1, key.Item2), key.Item3);
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawNodes(DrawingContext dc)
    {
        var neighbors = SelectedNode >= 0 ? _graph!.Neighborhood(SelectedNode, 1) : null;
        var order = Enumerable.Range(0, _graph!.Nodes.Count)
            .OrderBy(i => IsLit(i) ? 1 : 0).ThenBy(i => i == SelectedNode || i == HoverNode ? 1 : 0);
        foreach (var i in order)
        {
            var p = ToScreen(i);
            if (p.X < -30 || p.Y < -30 || p.X > ActualWidth + 30 || p.Y > ActualHeight + 30) continue;
            var node = _graph.Nodes[i];
            var r = Radius(i);
            var lit = IsLit(i);
            var color = ClusterColor(node.Cluster);
            var fill = BrushOf(color, lit ? (byte)235 : (byte)45);

            if (i == SelectedNode)
                dc.DrawEllipse(BrushOf(Accent, 60), null, p, r + 9, r + 9);
            else if (neighbors?.Contains(i) == true)
                dc.DrawEllipse(BrushOf(Accent, 28), null, p, r + 5, r + 5);

            var outline = i == SelectedNode ? SelectedOutline : i == HoverNode ? HoverOutline : NodeOutline;
            switch (node.Source)
            {
                case SemanticSource.Note:
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(p.X, p.Y - r * 1.2), true, true);
                        ctx.LineTo(new Point(p.X + r * 1.2, p.Y), true, false);
                        ctx.LineTo(new Point(p.X, p.Y + r * 1.2), true, false);
                        ctx.LineTo(new Point(p.X - r * 1.2, p.Y), true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(fill, outline, g);
                    break;
                case SemanticSource.Recording:
                    dc.DrawRoundedRectangle(fill, outline, new Rect(p.X - r * 0.9, p.Y - r * 0.9, r * 1.8, r * 1.8), 2, 2);
                    break;
                default:
                    dc.DrawEllipse(fill, outline, p, r, r);
                    break;
            }
        }
    }

    private void DrawClusterLabels(DrawingContext dc, double dpi)
    {
        var placed = new List<Rect>();
        foreach (var cluster in _graph!.Clusters.OrderByDescending(c => c.Members.Count))
        {
            if (cluster.Members.Count < 3) continue;
            var lit = cluster.Members.Any(IsLit);
            var cx = cluster.Members.Average(i => _x[i]) * _zoom + _offset.X;
            var cy = cluster.Members.Min(i => _y[i]) * _zoom + _offset.Y - 22;
            var color = ClusterColor(cluster.Id);
            var label = cluster.Label.Length > 32 ? cluster.Label[..32] + "…" : cluster.Label;
            var text = new FormattedText(label, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
                LabelFace, 12.5, BrushOf(color, lit ? (byte)255 : (byte)70), dpi);
            var rect = new Rect(Math.Clamp(cx - text.Width / 2 - 8, 4, Math.Max(4, ActualWidth - text.Width - 20)),
                cy - 3, text.Width + 16, text.Height + 6);
            var fits = false;
            foreach (var offset in new[] { 0, -28, 28, -56, 56 })
            {
                var candidate = rect;
                candidate.Offset(0, offset);
                if (candidate.Top < 2 || candidate.Bottom > ActualHeight - 2 || placed.Any(r => r.IntersectsWith(candidate))) continue;
                rect = candidate;
                fits = true;
                break;
            }
            if (!fits) continue;
            var reserved = rect;
            reserved.Inflate(4, 3);
            placed.Add(reserved);
            dc.DrawRoundedRectangle(BrushOf(Color.FromRgb(0x14, 0x14, 0x14), lit ? (byte)220 : (byte)90),
                new Pen(BrushOf(color, lit ? (byte)140 : (byte)40), 1), rect, 9, 9);
            dc.DrawText(text, new Point(rect.X + 8, rect.Y + 3));
        }

        // Category view: subcategories as smaller labels at the centre of their members, where space is left.
        foreach (var cluster in _graph.Clusters)
        {
            foreach (var sub in cluster.Subgroups.Where(s => s.Members.Count >= 3))
            {
                var lit = sub.Members.Any(IsLit);
                var color = ClusterColor(cluster.Id);
                var label = "› " + (sub.Name.Length > 28 ? sub.Name[..28] + "…" : sub.Name);
                var text = new FormattedText(label, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
                    LabelFace, 11, BrushOf(color, lit ? (byte)230 : (byte)60), dpi);
                var cx = sub.Members.Average(i => _x[i]) * _zoom + _offset.X;
                var cy = sub.Members.Average(i => _y[i]) * _zoom + _offset.Y;
                var rect = new Rect(cx - text.Width / 2 - 6, cy - text.Height / 2 - 2, text.Width + 12, text.Height + 4);
                if (rect.Top < 2 || rect.Bottom > ActualHeight - 2 || rect.Left < 2 || rect.Right > ActualWidth - 2
                    || placed.Any(r => r.IntersectsWith(rect))) continue;
                placed.Add(rect);
                dc.DrawRoundedRectangle(BrushOf(Color.FromRgb(0x14, 0x14, 0x14), lit ? (byte)200 : (byte)80),
                    new Pen(BrushOf(color, lit ? (byte)90 : (byte)30), 1), rect, 7, 7);
                dc.DrawText(text, new Point(rect.X + 6, rect.Y + 2));
            }
        }
    }

    private void DrawNodeLabels(DrawingContext dc, double dpi)
    {
        var labelled = new HashSet<int>();
        if (SelectedNode >= 0) labelled.Add(SelectedNode);
        if (HoverNode >= 0) labelled.Add(HoverNode);
        // Neighbor titles belong in the side list; drawing them all hides the selected hub.

        var occupied = new List<Rect>();
        foreach (var i in labelled.OrderByDescending(i => i == SelectedNode))
        {
            var p = ToScreen(i);
            if (p.X < -100 || p.Y < -30 || p.X > ActualWidth + 100 || p.Y > ActualHeight + 30) continue;
            var title = _graph!.Nodes[i].Item.Title;
            if (title.Length > 42) title = title[..42] + "…";
            var strong = i == SelectedNode || i == HoverNode;
            var text = new FormattedText(title, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
                strong ? LabelFace : NodeFace, strong ? 12.5 : 11, strong ? Brushes.White : BrushOf(Color.FromRgb(0xD8, 0xD2, 0xCA), 230), dpi);
            text.MaxTextWidth = Math.Max(40, ActualWidth - 20);
            text.MaxLineCount = 1;
            text.Trimming = TextTrimming.CharacterEllipsis;
            var at = new Point(Math.Clamp(p.X + Radius(i) + 6, 4, Math.Max(4, ActualWidth - text.Width - 5)),
                Math.Clamp(p.Y - text.Height / 2, 3, Math.Max(3, ActualHeight - text.Height - 3)));
            var box = new Rect(at.X - 3, at.Y - 1, text.Width + 6, text.Height + 2);
            if (occupied.Any(r => r.IntersectsWith(box)))
            {
                at.Y += text.Height + 6;
                box.Offset(0, text.Height + 6);
                if (box.Bottom > ActualHeight) continue;
            }
            occupied.Add(box);
            dc.DrawRoundedRectangle(BrushOf(Background, 230), null, box, 3, 3);
            dc.DrawText(text, at);
        }
    }

    // ---- Mouse -----------------------------------------------------------------------------------------------

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        var newZoom = Math.Clamp(_zoom * factor, 0.05, 6);
        // Keep the world point under the cursor in place.
        _offset = new Vector(p.X - (p.X - _offset.X) * newZoom / _zoom, p.Y - (p.Y - _offset.Y) * newZoom / _zoom);
        _zoom = newZoom;
        UserMovedView = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (e.ClickCount == 2)
        {
            var hit = NodeAt(p);
            if (hit >= 0) NodeDoubleClicked?.Invoke(hit);
            e.Handled = true;
            return;
        }
        _dragStart = p;
        _offsetAtDragStart = _offset;
        _moved = false;
        _dragNode = AllowNodeDrag ? NodeAt(p) : -1;
        _panning = _dragNode < 0;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            if ((p - _dragStart).Length > 3) _moved = true;
            if (!_moved) return;
            if (_dragNode >= 0)
            {
                _x[_dragNode] = (p.X - _offset.X) / _zoom;
                _y[_dragNode] = (p.Y - _offset.Y) / _zoom;
            }
            else if (_panning)
            {
                _offset = _offsetAtDragStart + (p - _dragStart);
                UserMovedView = true;
            }
            InvalidateVisual();
            return;
        }

        var hover = NodeAt(p);
        if (hover == HoverNode) return;
        HoverNode = hover;
        Cursor = hover >= 0 ? Cursors.Hand : Cursors.Arrow;
        ToolTip = hover >= 0 ? TooltipFor(hover) : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        if (!_moved)
        {
            var hit = NodeAt(e.GetPosition(this));
            SelectedNode = hit;
            InvalidateVisual();
            NodeClicked?.Invoke(hit);
        }
        _dragNode = -1;
        _panning = false;
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (HoverNode < 0) return;
        HoverNode = -1;
        InvalidateVisual();
    }

    private string TooltipFor(int i)
    {
        var node = _graph!.Nodes[i];
        var cluster = _graph.ClusterOf(i);
        return $"{node.Item.Title}\n{SemanticChunk.SourceName(node.Source)} · {node.Item.Date:dd.MM.yyyy HH:mm}"
               + (cluster != null ? $" · {cluster.Label}" : "")
               + $"\n{node.Degree} Verknüpfungen";
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);
}
