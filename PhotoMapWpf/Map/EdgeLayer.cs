using System.Windows;
using System.Windows.Media;

namespace PhotoMapWpf.Map;

/// Renders chronology lines (older -> newer) between visible cluster blocks.
/// OnRender = absolute canvas coordinates, no Shape rebasing quirks.
sealed class EdgeLayer : FrameworkElement {
    internal readonly record struct EdgeGeometry(Point From, Point To);

    public List<EdgeGeometry> Items { get; set; } = new();

    static readonly Pen UnderPen = MakePen(Color.FromArgb(0x66, 0x00, 0x00, 0x00), 3.6);
    static readonly Pen LinePen = MakePen(Color.FromRgb(0xF0, 0xB4, 0x29), 2);
    static readonly Brush HeadBrush = MakeBrush(Color.FromRgb(0xF0, 0xB4, 0x29));

    static Pen MakePen(Color c, double w) {
        var b = new SolidColorBrush(c);
        b.Freeze();
        var p = new Pen(b, w);
        p.Freeze();
        return p;
    }

    static Brush MakeBrush(Color c) {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected override void OnRender(DrawingContext dc) {
        foreach (var e in Items) {
            var v = e.To - e.From;
            if (v.Length < 4) continue;
            dc.DrawLine(UnderPen, e.From, e.To);
            dc.DrawLine(LinePen, e.From, e.To);
            double ux = v.X / v.Length, uy = v.Y / v.Length;
            double bx = e.To.X - ux * 9, by = e.To.Y - uy * 9;
            var geo = new StreamGeometry();
            using (var g = geo.Open()) {
                g.BeginFigure(e.To, true, true);
                g.LineTo(new Point(bx - uy * 3.4, by + ux * 3.4), true, false);
                g.LineTo(new Point(bx + uy * 3.4, by - ux * 3.4), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(HeadBrush, null, geo);
        }
    }
}