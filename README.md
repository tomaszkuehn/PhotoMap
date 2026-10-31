# PhotoMap

Windows desktop (WPF, .NET 8, pure native — no external NuGet packages) app that shows
photos from a selected folder on a map, based on the GPS position stored in EXIF.

A Tauri-based prototype (`src-tauri/`, `ui/`) was abandoned; the maintained application is
`PhotoMapWpf/`.

## Features

- **Map** — OpenStreetMap tiles (tile 256, zoom 2–19), pan/zoom via mouse and buttons.
- **Clustering** — grid clustering that splits as you zoom in (`CellSize(z)`: ≤11→256,
  ≤15→128, ≤17→64, else 40 px). Clusters show a proportional count frame
  (`FrameSize(n) = min(96, 32 + sqrt(n)·5)`); click zooms into the cluster.
  At zoom ≥ 18 clusters of ≤ 16 photos spiderfy with single 48 px photo thumbnails.
- **Chronology lines** — amber arrows drawn between neighbouring visible blocks
  (≤ 240 px), directed from **older → newer**. Photo time comes from EXIF
  `DateTimeOriginal`; if missing/unparsable, falls back to the file's last-modified time.
  Toggle with the **Lines: on/off** toolbar button (on by default).
- **Preview overlay** — click a single photo marker to see the photo, file name, EXIF date
  and coordinates. Closes only via **✕** or **Esc** (never via backdrop click).
- **Navigation ← / →** — moves through photos in **chronological order** by capture time
  (← older, → newer); counter `n/N (chronological)` shown in the overlay. The map follows.
- **Selection & export** — right-click or Ctrl+click a marker toggles one photo; right-click
  a frame toggles the whole cluster (green border = selected). **Export selected** copies
  files to a chosen folder (no overwrite; name collisions become `stem (k).ext`); selection
  survives the export. **S** in the overlay selects the current photo.
- Scanning is recursive; photos without GPS are ignored.
- **Trip suggestions** — with a catalog already opened, the **Suggest…** toolbar button lets
  the user pick a *trip folder* (photos taken during the trip). The app clusters the trip
  photos so those within **1 km** of each other form **one location**, then proposes for
  every location the catalog photos located within the same 1 km radius (files already
  present in the trip folder, trip photos themselves, and photos copied earlier this
  session are never proposed again in that session). The suggestion window shows thumbnails;
  clicking a tile enlarges the photo to window size (click again or **Esc** returns), where
  **S** selects/deselects and **← / →** browse the location's photos (older → newer).
  Locations to which photos were copied get a **+N newly added** marker in the list.
  Each tile has a **select** checkbox, and **Copy selected (N)** copies the chosen photos
  into the trip folder (same collision-safe naming as Export).

## Build, test, run

```powershell
# stop a running instance first (the exe locks bin output)
Stop-Process -Name PhotoMapWpf -Force -ErrorAction SilentlyContinue

dotnet test  PhotoMapWpf.Tests\PhotoMapWpf.Tests.csproj -v q
dotnet publish PhotoMapWpf\PhotoMapWpf.csproj -c Release -o PhotoMapWpf\publish -v q
Start-Process "PhotoMapWpf\publish\PhotoMapWpf.exe"
```

## Project layout

| Path | Purpose |
|---|---|
| `PhotoMapWpf/Map/MapControl.cs` | map rendering, clustering, pan/zoom, selection, chronology edges |
| `PhotoMapWpf/Map/EdgeLayer.cs` | custom-drawn (OnRender) chronology lines + arrowheads |
| `PhotoMapWpf/Map/Mercator.cs` | Web-Mercator math, `Geo.HaversineKm` |
| `PhotoMapWpf/Map/TileLoader.cs` | OSM tile HTTP fetch + `%LOCALAPPDATA%\PhotoMap\tiles` cache |
| `PhotoMapWpf/ExifParser.cs` | own TIFF parser: GPS, `DateTimeOriginal`, orientation |
| `PhotoMapWpf/Photo.cs` | photo model, `TryGetTicks` (EXIF date parsing) |
| `PhotoMapWpf/ScanService.cs` | recursive scan, fills `TimeTicks` (EXIF → file mtime) |
| `PhotoMapWpf/ThumbnailService.cs` | WPF thumbnail/preview decode + `%LOCALAPPDATA%\PhotoMap\thumbs` cache |
| `PhotoMapWpf/ExportService.cs` | copy-with-rename export |
| `PhotoMapWpf/MainWindow.xaml(.cs)` | toolbar (Open folder, Lines, Export, Clear), overlay, keyboard |
| `PhotoMapWpf.Tests/` | xUnit tests (EXIF corpus parsing, Mercator, export, date parsing) |

## Keyboard & mouse

| Action | Control |
|---|---|
| Open folder | toolbar button |
| Pan / zoom | drag / wheel / +/- buttons, cluster click zooms in |
| Open preview | left-click single photo marker |
| ← / → (overlay) | previous / next photo by capture time |
| S (overlay) | select/deselect current photo |
| Esc or ✕ (overlay) | close preview |
| Select one / cluster | right-click or Ctrl+click marker / frame |
| Export selected | toolbar button |