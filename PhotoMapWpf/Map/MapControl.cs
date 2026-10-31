using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace PhotoMapWpf.Map;

/// Native slippy map: OSM tiles + grid-based photo clustering.
/// Zooming in halves/quarters each cluster cell until single photo miniatures appear at max zoom.
public class MapControl : Grid {
    const int MinZoom = 2;
    const int MaxZoom = 19;
    const double Tile = 256;

    sealed class Cluster {
        public readonly List<Photo> Items = new();
        public double Lat;
        public double Lon;
        public double MeanTicks;   // chronology anchor for edge direction
    }

    sealed class Unit {
        public Photo? Photo;      // single-photo unit (clickable)
        public Cluster? Cluster;  // frame unit (click -> zoom in)
        public int Count;
        public double Lat;
        public double Lon;
        public double OffsetX;
        public double OffsetY;
        public double Size = 48;
        public FrameworkElement? Element;
    }

    readonly Canvas _tileLayer = new();
    readonly EdgeLayer _edgeLayer = new();
    readonly Canvas _markerLayer = new();
    readonly Dictionary<string, Image> _tiles = new();
    readonly TileLoader _tileLoader = new();
    readonly List<Unit> _units = new();
    List<Photo> _photos = new();

    int _zoom = 2;
    double _centerLat = 28, _centerLon = 8;
    Point _downPoint, _lastMove;
    bool _pointerDown, _captured, _moved;

    public event Action<Photo>? PhotoClicked;

    /// All currently loaded geotagged photos (navigation source for overlays).
    public IReadOnlyList<Photo> Photos => _photos;

    // ---- selection --------------------------------------------------------------

    readonly HashSet<Photo> _selected = new();

    static readonly Brush SelectedBrush = FreezeBrush(Color.FromRgb(0x35, 0xC4, 0x6A));

    public event Action? SelectionChanged;

    public bool IsSelected(Photo p) => _selected.Contains(p);
    public int SelectedCount => _selected.Count;

    public IReadOnlyList<Photo> GetSelected() => _selected.ToList();

    public void ToggleSelected(Photo p) {
        if (!_selected.Add(p)) _selected.Remove(p);
        OnSelectionChanged();
    }

    public void ClearSelection() {
        if (_selected.Count == 0) return;
        _selected.Clear();
        OnSelectionChanged();
    }

    void OnSelectionChanged() {
        foreach (var u in _units) {
            if (u.Element is Border b) ApplyUnitVisual(u, b);
        }
        SelectionChanged?.Invoke();
    }

    void ApplyUnitVisual(Unit u, Border b) {
        if (u.Photo != null) {
            bool sel = _selected.Contains(u.Photo);
            b.BorderBrush = sel ? SelectedBrush : Brushes.White;
            b.BorderThickness = sel ? new Thickness(3.5) : new Thickness(2.5);
        } else {
            bool all = u.Cluster!.Items.All(x => _selected.Contains(x));
            b.BorderBrush = all ? SelectedBrush : Brushes.White;
            b.BorderThickness = new Thickness(2);
        }
    }

    void SelectCluster(Cluster c, bool select) {
        foreach (var x in c.Items) {
            if (select) _selected.Add(x);
            else _selected.Remove(x);
        }
        OnSelectionChanged();
    }

    public void CenterOn(double lat, double lon) => SetView(lat, lon, _zoom);

    public MapControl() {
        ClipToBounds = true;
        Children.Add(_tileLayer);
        Children.Add(_edgeLayer);
        Children.Add(_markerLayer);
        _edgeLayer.IsHitTestVisible = false;
        SizeChanged += (_, _) => UpdateView();
    }

    // ---- chronology lines (older -> newer) --------------------------------------

    bool _showLines;

    public bool ShowLines {
        get => _showLines;
        set {
            if (_showLines == value) return;
            _showLines = value;
            UpdateView();
        }
    }

    sealed class ClusterPoint {
        public Cluster C = null!;
        public double X, Y, Radius;
    }

    readonly List<ClusterPoint> _visClusters = new();
    readonly HashSet<Cluster> _visSeen = new();

    void RecalcEdges() {
        var list = new List<EdgeLayer.EdgeGeometry>();
        int n = _visClusters.Count;
        for (int i = 0; i < n; i++) {
            var a = _visClusters[i];
            int bj = -1;
            double best = double.MaxValue;
            for (int j = 0; j < n; j++) {
                if (i == j) continue;
                var b = _visClusters[j];
                double ddx = a.X - b.X, ddy = a.Y - b.Y;
                double d = ddx * ddx + ddy * ddy;
                if (d < best) {
                    best = d;
                    bj = j;
                }
            }
            if (bj < 0 || best > 240 * 240) continue;   // only genuinely adjacent blocks
            var t2 = _visClusters[bj];

            // direction: from older block to newer block
            var (f, t) = a.C.MeanTicks <= t2.C.MeanTicks ? (a, t2) : (t2, a);
            double dx = t.X - f.X, dy = t.Y - f.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < f.Radius + t.Radius + 14) continue;
            double ux = dx / len, uy = dy / len;
            list.Add(new EdgeLayer.EdgeGeometry(
                new Point(f.X + ux * f.Radius, f.Y + uy * f.Radius),
                new Point(t.X - ux * t.Radius, t.Y - uy * t.Radius)));
        }
        _edgeLayer.Items = list;
        _edgeLayer.InvalidateVisual();
    }

    static int CellSize(int z) {
        if (z <= 11) return 256;
        if (z <= 15) return 128;
        if (z <= 17) return 64;
        return 40;
    }

    static double FrameSize(int n) => Math.Min(96, 32 + Math.Round(Math.Sqrt(n) * 5));

    static string FormatCount(int n) {
        if (n > 999) return (n / 1000.0).ToString("0.0") + "k";
        return n.ToString();
    }

    static readonly Effect MarkerShadow = CreateShadow();

    static Effect CreateShadow() {
        var e = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.45 };
        e.Freeze();
        return e;
    }

    static readonly Brush FrameBrush = FreezeBrush(Color.FromArgb(0xD9, 0x2F, 0x7D, 0xF6));
    static readonly Brush ThumbBg = FreezeBrush(Color.FromRgb(0x26, 0x2B, 0x33));

    static Brush FreezeBrush(Color c) {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public void SetPhotos(List<Photo> photos) {
        _photos = photos;
        _selected.Clear();
        SelectionChanged?.Invoke();
        RecalcClusters();
        UpdateView();
    }

    public void FitToPhotos() {
        if (_photos.Count == 0 || ActualWidth < 2) return;
        double minLat = 90, maxLat = -90, minLon = 180, maxLon = -180;
        foreach (var p in _photos) {
            minLat = Math.Min(minLat, p.Lat);
            maxLat = Math.Max(maxLat, p.Lat);
            minLon = Math.Min(minLon, p.Lon);
            maxLon = Math.Max(maxLon, p.Lon);
        }
        double availX = Math.Max(100, ActualWidth - 140);
        double availY = Math.Max(100, ActualHeight - 140);
        int best = MinZoom;
        for (int z = 18; z >= MinZoom; z--) {
            double wpx = (maxLon - minLon) / 360.0 * Mercator.WorldSize(z);
            double yTop = Mercator.LatLonToWorld(maxLat, 0, z).Y;
            double yBot = Mercator.LatLonToWorld(minLat, 0, z).Y;
            double hpx = yBot - yTop;
            if (wpx <= availX && hpx <= availY) {
                best = Math.Min(z, 14);
                break;
            }
        }
        SetView((minLat + maxLat) / 2, (minLon + maxLon) / 2, best);
    }

    void SetView(double lat, double lon, int zoom) {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        lat = Math.Clamp(lat, -85.0, 85.0);
        lon = (lon + 540.0) % 360.0 - 180.0;
        if (zoom != _zoom) {
            _zoom = zoom;
            RecalcClusters();
        }
        _centerLat = lat;
        _centerLon = lon;
        UpdateView();
    }

    (double X, double Y) CenterWorld() =>
        Mercator.LatLonToWorld(_centerLat, _centerLon, _zoom);

    (double X, double Y) TopLeftWorld() {
        var (cx, cy) = CenterWorld();
        return (cx - ActualWidth / 2, cy - ActualHeight / 2);
    }

    void UpdateView() {
        UpdateTiles();
        RepositionUnits();
    }

    // ---- clustering -------------------------------------------------------------

    void RecalcClusters() {
        _markerLayer.Children.Clear();
        _units.Clear();
        if (_photos.Count == 0) return;
        int z = _zoom;
        int cell = CellSize(z);

        var groups = new Dictionary<long, Cluster>();
        foreach (var p in _photos) {
            var (x, y) = Mercator.LatLonToWorld(p.Lat, p.Lon, z);
            long gx = (long)Math.Floor(x / cell);
            long gy = (long)Math.Floor(y / cell);
            long key = gx * 100_000_000L + gy;
            if (!groups.TryGetValue(key, out var c)) {
                c = new Cluster();
                groups[key] = c;
            }
            c.Items.Add(p);
        }

        foreach (var c in groups.Values) {
            double lat = 0, lon = 0;
            double tickSum = 0;
            foreach (var p in c.Items) {
                lat += p.Lat;
                lon += p.Lon;
                tickSum += p.TimeTicks;
            }
            int n = c.Items.Count;
            c.Lat = lat / n;
            c.Lon = lon / n;
            c.MeanTicks = tickSum / n;

            if (n == 1) {
                var p = c.Items[0];
                _units.Add(new Unit { Photo = p, Cluster = c, Lat = p.Lat, Lon = p.Lon, Size = 48 });
            } else if (z >= 18 && n <= 16) {
                for (int i = 0; i < n; i++) {
                    var p = c.Items[i];
                    double ox = 0, oy = 0;
                    if (i > 0) {
                        double a = i * 2.39996;   // golden angle
                        double r = 12 + 15 * Math.Sqrt(i);
                        ox = r * Math.Cos(a);
                        oy = r * Math.Sin(a);
                    }
                    _units.Add(new Unit {
                        Photo = p,
                        Cluster = c,
                        Lat = p.Lat,
                        Lon = p.Lon,
                        OffsetX = ox,
                        OffsetY = oy,
                        Size = 48,
                    });
                }
            } else {
                _units.Add(new Unit {
                    Cluster = c,
                    Count = n,
                    Lat = c.Lat,
                    Lon = c.Lon,
                    Size = FrameSize(n),
                });
            }
        }
    }

    void RepositionUnits() {
        _visClusters.Clear();
        _visSeen.Clear();
        if (_units.Count == 0) {
            if (_edgeLayer.Items.Count > 0) {
                _edgeLayer.Items.Clear();
                _edgeLayer.InvalidateVisual();
            }
            return;
        }
        var (tlx, tly) = TopLeftWorld();
        double w = ActualWidth, h = ActualHeight;
        const double margin = 100;

        foreach (var u in _units) {
            var (wx, wy) = Mercator.LatLonToWorld(u.Lat, u.Lon, _zoom);
            double baseX = wx - tlx, baseY = wy - tly;
            double sx = baseX + u.OffsetX;
            double sy = baseY + u.OffsetY;
            if (sx >= -margin && sy >= -margin && sx <= w + margin && sy <= h + margin) {
                if (u.Element == null) {
                    u.Element = CreateElement(u);
                    _markerLayer.Children.Add(u.Element);
                }
                Canvas.SetLeft(u.Element, sx - u.Size / 2);
                Canvas.SetTop(u.Element, sy - u.Size / 2);
                if (u.Cluster != null && _visSeen.Add(u.Cluster)) {
                    _visClusters.Add(new ClusterPoint {
                        C = u.Cluster,
                        X = baseX,
                        Y = baseY,
                        Radius = u.Photo != null ? 27 : u.Size / 2 + 3,
                    });
                }
            } else if (u.Element != null) {
                _markerLayer.Children.Remove(u.Element);
                u.Element = null;
            }
        }

        if (_showLines) {
            RecalcEdges();
        } else if (_edgeLayer.Items.Count > 0) {
            _edgeLayer.Items.Clear();
            _edgeLayer.InvalidateVisual();
        }
    }

    FrameworkElement CreateElement(Unit u) =>
        u.Photo != null ? CreatePhotoElement(u) : CreateFrameElement(u);

    FrameworkElement CreatePhotoElement(Unit u) {
        var p = u.Photo!;
        var img = new Image {
            Width = 41,
            Height = 41,
            Stretch = Stretch.UniformToFill,
            Tag = p,
        };
        img.Clip = new RectangleGeometry(new Rect(0, 0, 41, 41), 9, 9);
        var border = new Border {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(10),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2.5),
            Background = ThumbBg,
            Effect = MarkerShadow,
            Cursor = Cursors.Hand,
            Child = img,
            Tag = p,
        };
        border.MouseLeftButtonUp += (_, ev) => {
            ev.Handled = true;
            if (_moved || _captured) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                ToggleSelected(p);
                return;
            }
            PhotoClicked?.Invoke(p);
        };
        border.MouseRightButtonUp += (_, ev) => {
            ev.Handled = true;
            ToggleSelected(p);
        };
        ThumbnailService.LoadInto(p, img);
        ApplyUnitVisual(u, border);
        return border;
    }

    FrameworkElement CreateFrameElement(Unit u) {
        var c = u.Cluster!;
        var text = new TextBlock {
            Text = FormatCount(u.Count),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var border = new Border {
            Width = u.Size,
            Height = u.Size,
            CornerRadius = new CornerRadius(12),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            Background = FrameBrush,
            Effect = MarkerShadow,
            Cursor = Cursors.Hand,
            Child = text,
        };
        border.MouseLeftButtonUp += (_, ev) => {
            ev.Handled = true;
            if (!_moved && !_captured) SetView(c.Lat, c.Lon, _zoom + 1);
        };
        border.MouseRightButtonUp += (_, ev) => {
            ev.Handled = true;
            bool all = c.Items.All(x => _selected.Contains(x));
            SelectCluster(c, !all);
        };
        ApplyUnitVisual(u, border);
        return border;
    }

    // ---- interaction ------------------------------------------------------------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
        base.OnMouseLeftButtonDown(e);
        _pointerDown = true;
        _moved = false;
        _downPoint = _lastMove = e.GetPosition(this);
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (!_pointerDown) return;
        if (e.LeftButton != MouseButtonState.Pressed) {
            _pointerDown = false;
            return;
        }
        var pm = e.GetPosition(this);
        if (!_captured) {
            if (Math.Abs(pm.X - _downPoint.X) + Math.Abs(pm.Y - _downPoint.Y) < 4) return;
            _captured = true;
            CaptureMouse();
        }
        double dx = pm.X - _lastMove.X;
        double dy = pm.Y - _lastMove.Y;
        _moved = true;
        _lastMove = pm;
        PanBy(dx, dy);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) {
        base.OnMouseLeftButtonUp(e);
        _pointerDown = false;
        if (_captured) {
            ReleaseMouseCapture();
            _captured = false;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) {
        base.OnLostMouseCapture(e);
        _captured = false;
        _pointerDown = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e) {
        base.OnMouseWheel(e);
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    void PanBy(double dx, double dy) {
        var (tlx, tly) = TopLeftWorld();
        var (lat, lon) = Mercator.WorldToLatLon(
            tlx - dx + ActualWidth / 2, tly - dy + ActualHeight / 2, _zoom);
        _centerLat = Math.Clamp(lat, -85.0, 85.0);
        _centerLon = (lon + 540.0) % 360.0 - 180.0;
        UpdateView();
    }

    void ZoomAt(Point pt, int dz) {
        int z2 = Math.Clamp(_zoom + dz, MinZoom, MaxZoom);
        if (z2 == _zoom) return;
        var (tlx, tly) = TopLeftWorld();
        var (lat, lon) = Mercator.WorldToLatLon(tlx + pt.X, tly + pt.Y, _zoom);
        _zoom = z2;
        RecalcClusters();
        var (nx, ny) = Mercator.LatLonToWorld(lat, lon, z2);
        var (flat, flon) = Mercator.WorldToLatLon(
            nx - pt.X + ActualWidth / 2, ny - pt.Y + ActualHeight / 2, z2);
        _centerLat = Math.Clamp(flat, -85.0, 85.0);
        _centerLon = (flon + 540.0) % 360.0 - 180.0;
        UpdateView();
    }

    // ---- tiles ------------------------------------------------------------------

    void UpdateTiles() {
        if (ActualWidth < 1 || ActualHeight < 1) return;
        int z = _zoom;
        long n = 1L << z;
        var (tlx, tly) = TopLeftWorld();
        long x0 = (long)Math.Floor(tlx / Tile);
        long y0 = (long)Math.Floor(tly / Tile);
        long x1 = (long)Math.Floor((tlx + ActualWidth) / Tile);
        long y1 = (long)Math.Floor((tly + ActualHeight) / Tile);

        var keep = new HashSet<string>();
        for (long ty = y0; ty <= y1; ty++) {
            if (ty < 0 || ty >= n) continue;
            for (long tx = x0; tx <= x1; tx++) {
                if (tx < 0 || tx >= n) continue;
                string key = $"{z}/{tx}/{ty}";
                keep.Add(key);
                if (!_tiles.TryGetValue(key, out var img)) {
                    img = new Image { Width = Tile, Height = Tile, Tag = key };
                    _tiles[key] = img;
                    _tileLayer.Children.Add(img);
                    _tileLoader.LoadAsync(z, (int)tx, (int)ty, img, key);
                }
                Canvas.SetLeft(img, tx * Tile - tlx);
                Canvas.SetTop(img, ty * Tile - tly);
            }
        }

        List<string> stale = new();
        foreach (var kv in _tiles) {
            if (!keep.Contains(kv.Key)) stale.Add(kv.Key);
        }
        foreach (var k in stale) {
            var img = _tiles[k];
            _tiles.Remove(k);
            _tileLayer.Children.Remove(img);
        }
    }

    protected override Size MeasureOverride(Size constraint) {
        _tileLayer.Measure(constraint);
        _markerLayer.Measure(constraint);
        return base.MeasureOverride(constraint);
    }
}