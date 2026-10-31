using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PhotoMapWpf;

public partial class SuggestWindow : Window {
    const double RadiusKm = 1.0;

    readonly string _tripFolder;
    readonly List<TripLocation> _locations;
    readonly List<Photo> _others;
    readonly ISet<string> _copied;
    readonly HashSet<Photo> _selected = new();
    readonly List<Photo> _visible = new();
    readonly Dictionary<Photo, CheckBox> _cbs = new();
    readonly Dictionary<Photo, Border> _borders = new();
    readonly Dictionary<TripLocation, int> _added = new();
    TripLocation _cur = new();
    int _bigToken;
    Photo? _big;
    readonly bool _hasOther;
    int _addedOther;
    int _gridToken;
    LoadState? _curLoad;

    sealed class LoadState {
        public int Total;
        public int Done;
    }

    static readonly Brush BaseBorderBrush = Freeze(0x24, 0x2B, 0x34);
    static readonly Brush SelBorderBrush = Freeze(0x35, 0xC4, 0x6A);
    static readonly Brush FgBrush = Freeze(0xE8, 0xEC, 0xF1);
    static readonly Brush DimBrush = Freeze(0x9A, 0xA6, 0xB5);
    static readonly Brush CardBgBrush = Freeze(0x1A, 0x21, 0x2A);

    static Brush Freeze(byte r, byte g, byte b) {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public SuggestWindow(string tripFolder, List<TripLocation> locations, List<Photo>? others, ISet<string> copied) {
        InitializeComponent();
        _tripFolder = tripFolder;
        _locations = locations;
        _copied = copied;
        _others = others ?? new List<Photo>();
        _hasOther = _others.Count > 0;
        Title = "Trip suggestions — copy catalog photos to " + tripFolder;

        LocBox.ItemsSource = LocationLabels();
        if (_locations.Count > 0) LocBox.SelectedIndex = 0;
        else if (_hasOther) LocBox.SelectedIndex = _locations.Count;   // jump to the Other section

        Closing += (_, _) => _bigToken++;
    }

    static string DateSuffix(TripLocation l) {
        var ps = l.Travel.Select(p => p.TimeTicks).Where(t => t > 0).ToList();
        if (ps.Count == 0) return "";
        var d0 = new DateTime(ps.Min()).ToLocalTime();
        var d1 = new DateTime(ps.Max()).ToLocalTime();
        string s0 = d0.ToString("yyyy-MM-dd HH:mm");
        string s1 = d1 == d0.Date ? d1.ToString("HH:mm") : d1.ToString("yyyy-MM-dd HH:mm");
        return "  ·  " + s0 + (s1 != s0 ? " → " + s1 : "");
    }

    List<string> LocationLabels() {
        var labels = _locations
            .Select((l, i) => {
                var s = $"Location {i + 1} — {l.Travel.Count} trip photo(s)" + DateSuffix(l);
                if (_added.TryGetValue(l, out int n)) s += $"  ·  +{n} newly added";
                return s;
            })
            .ToList();
        if (_hasOther) {
            var s = $"★ Other places — trip period ±2 days ({_others.Count} photo(s))";
            if (_addedOther > 0) s += $"  ·  +{_addedOther} newly added";
            labels.Add(s);
        }
        return labels;
    }

    void RefreshLabels() {
        int idx = LocBox.SelectedIndex;
        LocBox.ItemsSource = LocationLabels();
        LocBox.SelectedIndex = idx >= 0 && idx < _locations.Count + (_hasOther ? 1 : 0) ? idx : -1;
    }

    void OnLocChanged(object sender, SelectionChangedEventArgs e) {
        BuildGrid();
    }

    void BuildGrid() {
        int idx = LocBox.SelectedIndex;
        if (idx < 0 || idx >= _locations.Count + (_hasOther ? 1 : 0)) return;
        bool other = idx >= _locations.Count;
        _cur = other ? new TripLocation() : _locations[idx];
        _visible.Clear();

        var source = other ? (IEnumerable<Photo>)_others
                           : _locations[idx].Candidates;
        var cands = source.Where(p => !_copied.Contains(p.Path)).ToList();
        Cards.Children.Clear();
        _cbs.Clear();
        _borders.Clear();
        _visible.Clear();
        _gridToken++;
        var st = new LoadState { Total = cands.Count };
        _curLoad = st;
        foreach (var p in cands) {
            Cards.Children.Add(Card(p, _gridToken, st));
            _visible.Add(p);
        }
        UpdateLoadBar(st);

        if (other) {
            LocInfo.Text = $"{_visible.Count} suggested · captured within the trip period ±2 days, "
                         + $"outside the 1 km zones of all locations"
                         + (_addedOther > 0 ? $" · +{_addedOther} newly added" : "");
        } else {
            int added = _added.TryGetValue(_cur, out var a) ? a : 0;
            LocInfo.Text = $"{_visible.Count} suggested · location center {_cur.Lat:F4}, {_cur.Lon:F4}"
                         + (added > 0 ? $" · +{added} newly added" : "");
        }
        EmptyHint.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCopyBtn();
    }

    UIElement Card(Photo p, int tok, LoadState st) {
        var img = new Image {
            Width = 170,
            Height = 125,
            Stretch = Stretch.Uniform,
            Tag = p,
            Cursor = Cursors.Hand,
        };
        var placeholder = new TextBlock {
            Text = "loading…",
            FontSize = 10,
            Foreground = DimBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var imgBox = new Grid { Width = 170, Height = 125 };
        imgBox.Children.Add(placeholder);
        imgBox.Children.Add(img);

        ThumbnailService.LoadInto(p, img,
            onDone: () => {
                placeholder.Visibility = Visibility.Collapsed;
                st.Done++;
                if (_curLoad == st && Enlarged.Visibility != Visibility.Visible) {
                    UpdateLoadBar(st);
                }
            },
            stale: () => _gridToken != tok);

        var name = new TextBlock {
            Text = p.Name,
            FontSize = 11,
            Foreground = FgBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 6, 0, 0),
            ToolTip = p.Name + (p.Taken != null ? " · " + p.Taken : ""),
        };
        var sub = new TextBlock {
            Text = (p.Taken ?? "") + $" · {p.Lat:F5}, {p.Lon:F5}",
            FontSize = 10,
            Foreground = DimBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var cb = new CheckBox {
            Content = "select",
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
           Foreground = FgBrush,
            Tag = p,
        };

        var root = new StackPanel();
        root.Children.Add(imgBox);
        root.Children.Add(name);
        root.Children.Add(sub);
        root.Children.Add(cb);

        var border = new Border {
            Width = 186,
            Margin = new Thickness(4),
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(6),
            BorderBrush = BaseBorderBrush,
            BorderThickness = new Thickness(2),
            Background = CardBgBrush,
            Cursor = Cursors.Hand,
            Child = root,
        };
        border.MouseLeftButtonUp += (_, ev) => {
            ev.Handled = true;
            Enlarge(p);
        };

        _cbs[p] = cb;
        _borders[p] = border;

        cb.Checked += (_, _) => { if (_selected.Add(p)) RefreshView(p); UpdateCopyBtn(); };
        cb.Unchecked += (_, _) => { _selected.Remove(p); RefreshView(p); UpdateCopyBtn(); };

        RefreshView(p);

        return border;
    }

    void RefreshView(Photo p) {
        bool sel = _selected.Contains(p);
        if (_borders.TryGetValue(p, out var border)) {
            border.BorderBrush = sel ? SelBorderBrush : BaseBorderBrush;
            border.BorderThickness = new Thickness(sel ? 3 : 2);
        }
        if (_cbs.TryGetValue(p, out var check)) check.IsChecked = sel;   // fires no event when unchanged
    }

    void Enlarge(Photo p) {
        _big = p;
        Enlarged.Visibility = Visibility.Visible;
        EnlargedImg.Source = null;
        SetStatus("Loading preview…");
        int token = ++_bigToken;
        int width = Math.Max(1200, (int)ActualWidth * 2);
        _ = ThumbnailService.LoadPreviewAsync(p.Path, p.Orientation, width).ContinueWith(t => {
            _ = Dispatcher.BeginInvoke(() => {
                if (token == _bigToken) {
                    EnlargedImg.Source = t.Result;
                    SetStatus(BigHint());
                }
            });
        });
    }

    string BigHint() {
        if (_big == null) return "";
        int idx = _visible.IndexOf(_big);
        bool sel = _selected.Contains(_big);
        return $"{idx + 1}/{_visible.Count} — S = {(sel ? "deselect" : "select")} · ← / → browse · click or Esc = back · {(_big.Name)}";
    }

    void OnEnlargedClick(object sender, MouseButtonEventArgs e) {
        CollapseEnlarged();
    }

    void CollapseEnlarged() {
        _bigToken++;
        _big = null;
        Enlarged.Visibility = Visibility.Collapsed;
        EnlargedImg.Source = null;
        SetStatus("");
    }

    void OnWindowKeyDown(object sender, KeyEventArgs e) {
        switch (e.Key) {
            case Key.Escape:
                if (Enlarged.Visibility == Visibility.Visible) {
                    CollapseEnlarged();
                } else {
                    Close();
                }
                e.Handled = true;
                break;
            case Key.S when Enlarged.Visibility == Visibility.Visible && _big != null: {
                var p = _big;
                if (_selected.Remove(p)) {
                    // was selected → deselect
                } else {
                    _selected.Add(p);
                }
                RefreshView(p);
                UpdateCopyBtn();
                SetStatus(BigHint());
                e.Handled = true;
                break;
            }
            case Key.Left when Enlarged.Visibility == Visibility.Visible: {
                int idx = _big != null ? _visible.IndexOf(_big) : -1;
                if (idx > 0) {
                    Enlarge(_visible[idx - 1]);
                    e.Handled = true;
                } else {
                    SetStatus("This is the first photo of this location");
                }
                break;
            }
            case Key.Right when Enlarged.Visibility == Visibility.Visible: {
                int idx = _big != null ? _visible.IndexOf(_big) : -1;
                if (idx >= 0 && idx < _visible.Count - 1) {
                    Enlarge(_visible[idx + 1]);
                    e.Handled = true;
                } else {
                    SetStatus("This is the last photo of this location");
                }
                break;
            }
        }
    }

    void SetStatus(string text) {
        StatusT.Text = text;
    }

    void UpdateLoadBar(LoadState st) {
        int remaining = st.Total - st.Done;
        bool loading = remaining > 0;
        LoadBar.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        if (loading) {
            SetStatus($"Loading thumbnails… {st.Done}/{st.Total} loaded ({remaining} remaining)");
        } else if (st.Total > 0) {
            SetStatus($"All {st.Total} thumbnails loaded — click a tile to enlarge, S = select");
        }
    }

    void UpdateCopyBtn() {
        CopyBtn.Content = $"Copy selected ({_selected.Count})";
        CopyBtn.IsEnabled = _selected.Count > 0;
    }

    async void OnCopyClick(object sender, RoutedEventArgs e) {
        var items = _selected.ToList();
        if (items.Count == 0) return;
        CopyBtn.IsEnabled = false;
        SetStatus($"Copying {items.Count} to {_tripFolder} …");
        try {
            var progress = new Progress<(int Copied, int Failed, int Total)>(v =>
                SetStatus($"Copying… {v.Copied}/{v.Total} copied" + (v.Failed > 0 ? $", {v.Failed} failed" : "")));
            var res = await Task.Run(() => ExportService.Copy(items, _tripFolder, progress));
            if (res.Failed == 0) {
                SetStatus($"Copied {res.Copied} to {_tripFolder}");
            } else {
                SetStatus($"Copied {res.Copied}, {res.Failed} failed — last error: {res.LastError}");
            }
            foreach (var p in items) {
                _copied.Add(p.Path);   // never proposed again in this session
                _selected.Remove(p);
            }

            // mark locations that gained new photos
            foreach (var l in _locations) {
                int n = items.Count(i => l.Candidates.Contains(i));
                if (n > 0) _added[l] = _added.TryGetValue(l, out var prev) ? prev + n : n;
            }
            _addedOther += items.Count(i => _others.Contains(i));
            RefreshLabels();
            BuildGrid();
        } catch (Exception ex) {
            SetStatus("Error: " + ex.Message);
        } finally {
            CopyBtn.IsEnabled = _selected.Count > 0;
        }
    }
}