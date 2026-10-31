using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PhotoMapWpf.Map;

namespace PhotoMapWpf;

public partial class MainWindow : Window {
    int _overlayToken;
    Photo? _navAnchor;
    List<Photo> _navOrder = new();
    int _navIndex;
    bool _hintShown;
    bool _exporting;

    public MainWindow() {
        InitializeComponent();
        Map.PhotoClicked += OnMapPhotoClicked;
        Map.SelectionChanged += UpdateSelectionUi;
        Map.ShowLines = true;
        UpdateSelectionUi();
    }

    static readonly Brush AccentBg = MakeSolid(Color.FromRgb(0x2F, 0x7D, 0xF6));
    static readonly Brush DarkBg = MakeSolid(Color.FromRgb(0x24, 0x2B, 0x34));

    static Brush MakeSolid(Color c) {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    void OnLinesBtnClick(object sender, RoutedEventArgs e) {
        Map.ShowLines = !Map.ShowLines;
        UpdateLinesUi();
    }

    void UpdateLinesUi() {
        bool on = Map.ShowLines;
        LinesBtn.Content = on ? "Lines: on" : "Lines: off";
        LinesBtn.Background = on ? AccentBg : DarkBg;
        LinesBtn.Foreground = Brushes.White;
        LinesBtn.IsEnabled = Map.Photos.Count > 0 && !_exporting;
    }

    void UpdateSelectionUi() {
        int n = Map.SelectedCount;
        SelCount.Text = $"Selected: {n:N0}";
        ExportBtn.IsEnabled = n > 0 && !_exporting;
        ClearBtn.IsEnabled = n > 0 && !_exporting;
        TripBtn.IsEnabled = Map.Photos.Count > 0 && !_exporting;
        UpdateLinesUi();
    }

    void OnClearSelectionClick(object sender, RoutedEventArgs e) {
        Map.ClearSelection();
    }

    // ---- trip suggestions --------------------------------------------------------

    readonly HashSet<string> _tripCopied = new(StringComparer.OrdinalIgnoreCase);

    async void OnTripSuggestClick(object sender, RoutedEventArgs e) {
        var dlg = new Microsoft.Win32.OpenFolderDialog {
            Title = "Select the TRIP folder (its photos define the locations)",
        };
        if (dlg.ShowDialog(this) != true) return;
        string trip = dlg.FolderName;

        TripBtn.IsEnabled = false;
        SetStatus($"Scanning trip folder {trip} …");
        try {
            var progress = new Progress<(int Scanned, int Mapped)>(v =>
                SetStatus($"Scanning trip folder… {v.Scanned:N0} files checked, {v.Mapped:N0} geotagged"));
            var outcome = await Task.Run(() => ScanService.Scan(trip, progress));
            if (outcome.Photos.Count == 0) {
                SetStatus("No geotagged photos found in the trip folder — nothing to suggest");
                return;
            }

            var catalog = Map.Photos.ToList();
            _tripCopied.Clear();
            var locs = await Task.Run(() => TripService.Build(outcome.Photos, catalog, trip, _tripCopied));
            var others = await Task.Run(() => TripService.BuildOther(outcome.Photos, catalog, trip, _tripCopied, locs));
            int totalCands = locs.Sum(l => l.Candidates.Count) + others.Count;
            SetStatus($"{locs.Count} location(s) in trip folder, {totalCands:N0} catalog photo(s) nearby — pick and copy");

            new SuggestWindow(trip, locs, others, _tripCopied) { Owner = this }.ShowDialog();
        } catch (Exception ex) {
            SetStatus("Error: " + ex.Message);
        } finally {
            UpdateSelectionUi();
        }
    }

    async void OnExportSelectedClick(object sender, RoutedEventArgs e) {
        var selected = Map.GetSelected().ToList();
        if (selected.Count == 0) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select destination folder for export" };
        if (dlg.ShowDialog(this) != true) return;
        string dest = dlg.FolderName;

        _exporting = true;
        UpdateSelectionUi();
        SetStatus($"Exporting {selected.Count:N0} photos to {dest} …");
        try {
            var progress = new Progress<(int Copied, int Failed, int Total)>(v =>
                SetStatus($"Exporting… {v.Copied:N0}/{v.Total:N0} copied" + (v.Failed > 0 ? $", {v.Failed:N0} failed" : "")));
            var res = await Task.Run(() => ExportService.Copy(selected, dest, progress));
            if (res.Failed == 0) {
                SetStatus($"Export done: {res.Copied:N0} copied to {dest}");
            } else {
                SetStatus($"Export done: {res.Copied:N0} copied, {res.Failed:N0} failed — last error: {res.LastError}");
            }
        } catch (Exception ex) {
            SetStatus("Export error: " + ex.Message);
        } finally {
            _exporting = false;
            UpdateSelectionUi();
        }
    }

    async void OnOpenFolderClick(object sender, RoutedEventArgs e) {
        var dlg = new Microsoft.Win32.OpenFolderDialog {
            Title = "Select folder with photos",
        };
        if (dlg.ShowDialog(this) != true) return;
        string folder = dlg.FolderName;

        OpenBtn.IsEnabled = false;
        SetStatus($"Scanning {folder} …");
        try {
            var progress = new Progress<(int Scanned, int Mapped)>(v =>
                SetStatus($"Scanning… {v.Scanned:N0} files checked, {v.Mapped:N0} geotagged"));
            var outcome = await Task.Run(() => ScanService.Scan(folder, progress));
            Map.SetPhotos(outcome.Photos);
            if (outcome.Photos.Count > 0) {
                Map.FitToPhotos();
                SetStatus($"{outcome.Photos.Count:N0} of {outcome.Scanned:N0} photos have GPS — click clusters to zoom in");
            } else {
                SetStatus($"No geotagged photos found ({outcome.Scanned:N0} scanned; photos without GPS are ignored)");
            }
        } catch (Exception ex) {
            SetStatus("Error: " + ex.Message);
        } finally {
            OpenBtn.IsEnabled = true;
        }
    }

    void SetStatus(string text) {
        Status.Text = text;
    }

    void OnMapPhotoClicked(Photo photo) {
        ShowOverlay(photo, resetNavigation: true);
    }

    async void ShowOverlay(Photo p, bool resetNavigation = true) {
        if (resetNavigation) {
            _navAnchor = p;
            _navOrder = Map.Photos.OrderBy(q => q.TimeTicks).ToList();
            _navIndex = Math.Max(0, _navOrder.IndexOf(p));
            if (!_hintShown) {
                _hintShown = true;
                SetStatus("Preview open — ← / → by photo date (← older, → newer) · S = select · Esc = close");
            }
        }
        Map.CenterOn(p.Lat, p.Lon);
        UpdateOverlayTexts(p);

        OvImg.Source = null;
        OvImg.MaxWidth = Math.Max(400, ActualWidth * 0.90);
        OvImg.MaxHeight = Math.Max(300, ActualHeight * 0.72);
        Overlay.Visibility = Visibility.Visible;
        int token = ++_overlayToken;
        var src = await ThumbnailService.LoadPreviewAsync(p.Path, p.Orientation, Math.Max(800, (int)OvImg.MaxWidth));
        if (token == _overlayToken && Overlay.Visibility == Visibility.Visible && src != null) {
            OvImg.Source = src;
        }
    }

    void UpdateOverlayTexts(Photo p) {
        bool sel = Map.IsSelected(p);
        OvName.Text = (sel ? "\u2713 " : "") + p.Name;
        var meta = (p.Taken is { Length: > 0 } t ? t + " · " : "") + $"{p.Lat:F5}, {p.Lon:F5}";
        if (_navOrder.Count > 1) meta += $"  ·  {_navIndex + 1}/{_navOrder.Count} (chronological)";
        if (sel) meta += "  ·  SELECTED";
        OvMeta.Text = meta;
    }

    void CloseOverlay() {
        _overlayToken++;
        Overlay.Visibility = Visibility.Collapsed;
        OvImg.Source = null;
    }

    void OnOvCloseClick(object sender, RoutedEventArgs e) => CloseOverlay();
    // Kliknięcie w tło/obraz NIE zamyka podglądu — zamyka tylko przycisk ✕, Esc, albo ←/→.

    void OnPreviewKeyDown(object sender, KeyEventArgs e) {
        if (Overlay.Visibility != Visibility.Visible) return;
        switch (e.Key) {
            case Key.Escape:
                CloseOverlay();
                e.Handled = true;
                break;
            case Key.S:
                if (_navIndex >= 0 && _navIndex < _navOrder.Count) {
                    var cur = _navOrder[_navIndex];
                    Map.ToggleSelected(cur);
                    UpdateOverlayTexts(cur);
                }
                e.Handled = true;
                break;
            case Key.Left:
                if (_navIndex > 0) ShowOverlay(_navOrder[--_navIndex], resetNavigation: false);
                e.Handled = true;
                break;
            case Key.Right:
                if (_navIndex < _navOrder.Count - 1) ShowOverlay(_navOrder[++_navIndex], resetNavigation: false);
                e.Handled = true;
                break;
        }
    }
}