<!-- ijfw-schema: v1 -->
# Knowledge Base
---
type: decision
summary: PhotoMap: Tauri 2 desktop app for photos-on-map from EXIF GPS
stored: 2026-10-31T10:42:29.268Z
hash: 50f23e150d9b
tags: [photomap, tauri, exif, leafelt-clustering, rust, windows-desktop]
---
<!-- hash:50f23e150d9b -->
PhotoMap (D:\kody\PhotoMap): Windows desktop app built with Tauri 2 (Rust backend + static HTML/JS/Leaflet frontend, no bundler). User chose Windows desktop over web/Android. Rust: kamadak-exif 0.6 for GPS/DateTime parse (crate is version 0.6.x, NOT 0.15; exif package name on crates.io is "kamadak-exif"), image 0.25 thumbnails with EXIF-orientation fix, custom async URI scheme protocol "photo" (http://photo.localhost/t/&lt;b64url path&gt; thumbs, /f/ full file) instead of asset-protocol scope. Clustering: custom grid split (256/128/64/40px cells by zoom) in ui/main.js, spiderfy at z&gt;=18 for &lt;=16 near-coincident photos, single photo click -&gt; overlay over map. Thumbnails cached to %LOCALAPPDATA%/PhotoMap/thumbs keyed by FNV-1a of path. tauri feature is "custom-protocol". WindowsFilePath: dialog FilePath.into_path() works.

**Why:** Platform choice (Windows desktop) and architecture decisions need to survive context resets to keep iterating consistently.
---
type: decision
summary: PhotoMap v2: pure native WPF/.NET 8, zero packages; Tauri rejected
stored: 2026-10-31T10:50:17.567Z
hash: 41c448324ca6
tags: [photomap, wpf, dotnet8, native, exif-parser, tiles, clustering]
---
<!-- hash:41c448324ca6 -->
PhotoMap (D:\kody\PhotoMap): user rejected the Tauri version despite it being a native exe — wanted pure native WPF/.NET (no HTML/JS layer). Rebuilt as PhotoMapWpf: .NET 8 WPF, zero external packages. Map = custom control (MapControl.cs): OSM tiles via HttpClient + disk cache %LOCALAPPDATA%\PhotoMap\tiles, own pan/zoom (drag + anchored wheel zoom), grid clustering identical to v1 (256/128/64/40px cells, spiderfy z&gt;=18&lt;=16). EXIF: own TIFF parser (ExifParser.cs, II+MM byte order, GPS/DateTimeOriginal/Orientation) w/ synthetic-jpeg xunit corpus in PhotoMapWpf.Tests (11 tests). Thumbnails: WPF imaging DecodePixelWidth=340 + TransformedBitmap orientation, JPEG q82 disk cache %LOCALAPPDATA%\PhotoMap\thumbs (FNV-1a path+size+mtime). Folder picker = Microsoft.Win32.OpenFolderDialog (.NET 8). Tauri project left in place at src-tauri/ (not removed). Gotchas: wpftmp project misses SOME implicit usings (System.IO) — always add explicit usings; DispatcherOperation/Task in fire-and-forget needs `_ =` discard to silence CS4014.

**Why:** Rebuild rationale and platform constraints must survive context resets for future iterations.

**How to apply:** Never reintroduce web-layer UI for this project; extend PhotoMapWpf only. Reuse ExifParser/ThumbnailService patterns for any new photo feature.
---
type: decision
summary: Overlay closes only via Esc or X button — never backdrop click
stored: 2026-10-31T13:02:42.700Z
hash: 6e291ed5322e
tags: [photomap, wpf, overlay-ux, fix]
---
<!-- hash:6e291ed5322e -->
PhotoMap WPF UX quirk fixed (2026-10-31): user reported that any click after opening photo overlay closed it ("po otwarciu zdjecia do podgladu kolejne klikniecie zamyka podglad"). Expected UX: clicks must NOT close the preview. Fix: removed backdrop-click close and inner-click swallow from MainWindow; overlay closes ONLY via Esc key or the ✕ button (OnOvCloseClick / Esc in OnPreviewKeyDown). Do not re-add backdrop-click close. Rebuild requires stopping PhotoMapWpf.exe first (running app locks bin exe — MSB3027); always Stop-Process -Name PhotoMapWpf before dotnet publish, then relaunch exe for the user.

**Why:** User explicitly wants accidental-click protection on the photo preview; rebuilding/locking pitfalls recur.

**How to apply:** Keep overlay close logic minimal (Esc + button). Before any PhotoMapWpf publish, kill the running process, then restart the published exe automatically.
---
type: decision
summary: Selection via right-click/Ctrl+click/S; export copies to chosen folder
stored: 2026-10-31T13:08:02.040Z
hash: 479f040ca814
tags: [photomap, wpf, selection, export, ux]
---
<!-- hash:479f040ca814 -->
PhotoMap WPF selection+export feature (2026-10-31): selection = HashSet&lt;Photo&gt; in MapControl (reference equality, cleared on SetPhotos/rescan). Interactions: right-click miniature toggles selection; Ctrl+Click miniature toggles instead of opening preview; right-click cluster frame toggles ALL photos in cluster (all-selected -&gt; deselect); overlay key S toggles current photo; green #35C46A border marks selected markers, overlay name gets checkmark + "SELECTED" in meta. Toolbar: SelCount TextBlock + Clear + "Export selected…" buttons (disabled when 0 or during export). Export = ExportService.Copy (public static; File.Copy, no overwrite, collision -&gt; "stem (k).ext" up to 999 then GUID), progress via IProgress&lt;(Copied,Failed,Total)&gt;, failures collected not thrown; destination via OpenFolderDialog. Selection survives export (explicit Clear button). WPF UI default button template kept.

**Why:** Interaction model must stay consistent for future UX additions.

**How to apply:** Keep selection state centralized in MapControl (reference-based Photo identity). Never auto-clear selection after export. New marker visuals must call ApplyUnitVisual.
---
type: decision
summary: Area filter: drag rect on map limits loaded photos to bbox
stored: 2026-10-31T13:25:32.698Z
hash: 4d3a9689136b
tags: [photomap, wpf, area-filter, bbox, ux]
---
<!-- hash:4d3a9689136b -->
PhotoMap WPF area filter (2026-10-31): "Area filter" button -&gt; MapControl.AreaDrawingMode=true -&gt; left-drag draws dashed blue rectangle (capture immediately, wheel zoom suppressed mid-drag, tiny (&lt;8px) drag = cancel, Esc cancels). On MouseUp computes AreaBbox (LatMin/LatMax/LonMin/LonMax, inclusive) via WorldToLatLon both corners; persistent outline repositioned every UpdateView through RepositionAreaRect (bbox-&gt;world px, lives in _areaLayer below markers, IsHitTestVisible=false). MainWindow filters via PhotoFilter.Within(_allPhotos, area) -&gt; Map.SetPhotos(filtered) + FitToPhotos. Full set kept in MainWindow._allPhotos; "Redraw area" replaces, "Reset area" restores all. New folder load clears area filter. Selection is cleared by SetPhotos (area change wipes selection). Button states: Area filter / Cancel / Redraw area; ResetAreaBtn enabled only with active filter. Tests: AreaFilterTests (boundary-inclusive).

**Why:** Geometry/state flow (bbox vs screen rect, full vs filtered sets) must be preserved in future edits.

**How to apply:** Filtering always goes through PhotoFilter.Within using inclusive AreaBbox; never filter in UI code. RepositionAreaRect is the single place converting bbox to screen.
---
type: decision
summary: Area filter removed at user request; feature set finalized
stored: 2026-10-31T13:40:36.729Z
hash: 10199291b08c
tags: [photomap, wpf, removed-feature, ux]
---
<!-- hash:10199291b08c -->
PhotoMap WPF (2026-10-31): area filter feature REMOVED at user's request ("usun ten filtr, nie wnosi nic nowego") one turn after adding it. Deleted: MapControl area region (AreaBbox struct, AreaDrawingMode, drag-rect drawing, RepositionAreaRect, _areaLayer), MainWindow area fields/handlers/buttons (AreaBtn, ResetAreaBtn), PhotoFilter.cs, AreaFilterTests. Do NOT re-add area filtering unless explicitly requested again. Remaining feature set: clustering, singles, preview overlay (Esc/✕ only), ←/→ nearest-first navigation, selection (PPM/Ctrl+click/S/cluster PPM), export copy to folder. 16 tests pass.

**Why:** Prevents re-adding rejected functionality; documents final feature scope.

**How to apply:** Treat area filtering as banned UX for this app; extend other aspects (clustering, navigation, export) instead.
---
type: decision
summary: Chronology lines: amber arrows older-&gt;newer between nearest clusters
stored: 2026-10-31T13:48:23.683Z
hash: 5a74514c86dc
tags: [photomap, wpf, chronology, edges, onrender]
---
<!-- hash:5a74514c86dc -->
PhotoMap WPF chronology lines (2026-10-31): EdgeLayer.cs = custom FrameworkElement with OnRender only (WPF Shape rebasing is a trap — never use Line/Path with negative coords on Canvas; OnRender/DrawingContext takes absolute points safely). Lines = amber #F0B429 (2px + dark under-pen 3.6px for tiles contrast), arrowhead StreamGeometry triangle at the NEWER end, drawn older-&gt;newer. Model: Photo.TimeTicks (EXIF DateTimeOriginal via Photo.TryGetTicks formats yyyy:MM:dd HH:mm:ss / yyyy-MM-dd HH:mm:ss / .fff, fallback FileInfo.LastWriteTimeUtc when EXIF date missing/unparsable), Cluster.MeanTicks = mean. Edge picking: each visible cluster connects to its nearest visible cluster (O(n^2) over visible-only, px dist &lt;= 240, skip pairs closer than radius sum +14; endpoint = cluster base pos without spider offset, radius = 27 for photo-thumb units / Size/2+3 for frames). Toggle: MapControl.ShowLines (default true, set in MainWindow ctor), LinesBtn button "Lines: on/off" accent when on; state managed in UpdateLinesUi; recompute per RepositionUnits (every pan/zoom). Layers: tiles &lt; edges &lt; markers; edge layer IsHitTestVisible=false. 18 tests.

**Why:** OnRender-vs-Shape lesson and edge params must persist for tuning.

**How to apply:** Tune edge behavior only via MapControl.RecalcEdges params (threshold 240px, radii, colors in EdgeLayer). Add any time-based logic on Photo.TimeTicks, never re-parse EXIF.
---
type: decision
summary: Trip suggestions: 1km clustering + candidate window, copy back
stored: 2026-10-31T14:45:58.095Z
hash: 8df1ceb286db
tags: [photomap, wpf, trip, clustering, suggestwindow]
---
<!-- hash:8df1ceb286db -->
PhotoMap trip suggestions (2026-10-31): workflow = main catalog via "Open folder…" + TripBtn "Suggest…" → OpenFolderDialog pick TRIP folder → ScanService.Scan(trip) → TripService.Build(tripPhotos, catalog, tripFolder, copiedSet): greedy clustering — trip photo joins FIRST location whose mean center is within 1 km (center recomputed as running mean; chain clustering intentional) else new TripLocation; locations ordered chronologically by MeanTicks. Candidates = catalog photos within 1 km of center, EXCLUDING: photos with same path as trip photos, same NAME already anywhere in trip folder (recursive GetFiles check), and session-copied paths (HashSet&lt;string&gt; passed by reference from MainWindow._tripCopied, persists across SuggestWindow opens). SuggestWindow (modal, Owner=main): ComboBox location selector "(Location k — N trip photo(s) · date range)", WrapPanel cards 186px (Border bg #1A212A + Image 170x125 via ThumbnailService.LoadInto + name/sub + CheckBox select, green #35C46A 3px border when selected, border click → Enlarge via ThumbnailService.LoadPreviewAsync width=max(1200, ActualWidth*2), token pattern _bigToken, overlay Border RowSpan=3, click/Esc dismisses, Esc otherwise closes window). Copy = ExportService.Copy(selected, tripFolder) then copied.Add(path), _selected.Remove, BuildGrid. WPF gotchas: Color.FromHex does NOT exist (MAUI only) — use Color.FromRgb; Image has NO Background property (use wrapper Border for click area); HashSet&lt;Photo&gt;.Id equality trap — photos built in tests share Id="" so trip-exclusion compares Path not Id.

**Why:** Feature per user: propose catalog photos per trip location, avoid repeated suggestions near close trip photos.

**How to apply:** Extend only TripService.Build filters and SuggestWindow grid; keep 1 km radius constant (both clustering and candidates) unless user asks.
---
type: decision
summary: Suggest preview: S-select, arrows, added-marker on locations
stored: 2026-10-31T14:58:14.785Z
hash: f545e505daab
tags: [photomap, wpf, suggestwindow, preview, selection]
---
<!-- hash:f545e505daab -->
PhotoMap SuggestWindow preview interactions (2026-10-31): enlarged preview adds S = toggle select + ←/→ browse within _visible (current location's filtered candidates, chronological order, no wrap — edge shows "first/last photo" status). Selection state single source = HashSet&lt;Photo&gt; _selected; UI refresh centralized in RefreshView(p) via _borders/_cbs dictionaries (cleared in BuildGrid, registered in Card) — checkbox Checked/Unchecked events and keyboard S both funnel through it (setting cb.IsChecked with same value fires no event). Location list = ComboBox ItemsSource List&lt;string&gt; rebuilt via LocationLabels()+RefreshLabels() preserving SelectedIndex; _added Dictionary&lt;TripLocation,int&gt; records copied counts ("+N newly added" suffix + LocInfo suffix) computed in OnCopyClick by counting items per location. Modal SuggestWindow keeps focus for PreviewKeyDown (Esc: enlarge→back, else Close).

**Why:** User asked for keyboard-driven review flow inside trip suggestion preview.

**How to apply:** Extend preview UX only inside SuggestWindow via _visible/_big/RefreshView; keep no-wrap arrow behavior.
---
type: decision
summary: Other places: trip-window ±2d outside location zones
stored: 2026-10-31T15:23:09.464Z
hash: 6501e36b110a
tags: [photomap, wpf, trip, other-section]
---
<!-- hash:6501e36b110a -->
PhotoMap "Other places" section (2026-10-31): TripService.BuildOther(trip, catalog, tripFolder, copied, locations, radiusKm=1, days=2) — window = [min(trip TimeTicks)−2d, max(trip TimeTicks)+2d]; catalog photos with TimeTicks outside (or ==0, unknown date → excluded) are skipped; further excludes trip paths/names-in-trip-folder/session-copied; final filter NOT within 1 km of ANY location center; sorted chronologically. No date on ALL trip photos → empty section. SuggestWindow: ComboBox index _locations.Count = sentinel "★ Other places — trip period ±2 days (N photo(s))"; BuildGrid branches other-mode (source _others), _addedOther int counter mirrors _added dict for "+N newly added" (both label + LocInfo). Selection/copy flow identical (selection is global across sections; copied paths land in shared _copied set). MainWindow computes others after locs and passes as 3rd ctor arg (nullable).

**Why:** User wanted en-route catalog photos from the trip period even when geographically detached from known stops.

**How to apply:** Reuse BuildOther for any future date-window suggestions; section sentinel = index == _locations.Count in SuggestWindow.
---
type: decision
summary: Bounded 4-worker FIFO thumb queue + loading estimations
stored: 2026-10-31T15:30:25.009Z
hash: a84eea6b128b
tags: [photomap, wpf, thumbnails, queue, performance]
---
<!-- hash:a84eea6b128b -->
PhotoMap thumbnail streaming fix (2026-10-31): root cause of empty tiles + slow loading = per-photo Task.Run flooding the thread pool with unbounded concurrent decodes (order arbitrary, cache misses thrash). Fix in ThumbnailService.LoadInto: bounded FIFO BlockingCollection + 4 LongRunning worker loops (EnsureWorkers via Interlocked one-shot), requests {Photo, Apply(Dispatcher), OnDone, Stale predicate}; stale check drops queued decodes cheaply (no decode) and still invokes OnDone so counters finish. SuggestWindow per-build LoadState{Total,Done} guarded by window _gridToken captured BEFORE _gridToken++ — CRITICAL ordering: increment token THEN create cards, pass tok to Card(p, tok, st); comparing st with _curLoad prevents old-build callbacks from polluting new counts. UI feedback: per-tile "loading…" TextBlock placeholder (behind Image in Grid), global indeterminate ProgressBar (LoadBar), status "Loading thumbnails… k/N (M remaining)" — suppressed while Enlarged preview open to not clobber its hint. MapControl grid thumbs also stream via the same pool now.

**Why:** Large trip locations showed many empty tiles then slow random loads.

**How to apply:** Tune Workers const in ThumbnailService; reuse load-state pattern (token+state object) for any future dynamic grids.
