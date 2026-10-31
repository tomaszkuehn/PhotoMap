using System.IO;

namespace PhotoMapWpf;

internal record ScanOutcome(List<Photo> Photos, int Scanned);

internal static class ScanService {
    public static ScanOutcome Scan(string folder, IProgress<(int Scanned, int Mapped)>? progress) {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        var photos = new List<Photo>();
        int scanned = 0, mapped = 0;
        foreach (var file in Walk(folder)) {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg")) continue;
            scanned++;
            var r = ExifParser.Parse(file);
            if (r != null) {
                var fi = new FileInfo(file);
                long ticks = Photo.TryGetTicks(r.Taken)
                             ?? (fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0);
                photos.Add(new Photo {
                    Id = Hashing.FnvHex(file),
                    Name = Path.GetFileName(file),
                    Path = file,
                    Lat = r.Lat,
                    Lon = r.Lon,
                    Taken = r.Taken,
                    Orientation = r.Orientation,
                    TimeTicks = ticks,
                });
                mapped++;
            }
            if (scanned % 250 == 0) progress?.Report((scanned, mapped));
        }
        return new ScanOutcome(photos, scanned);
    }

    static IEnumerable<string> Walk(string root) {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0) {
            var dir = stack.Pop();
            string[] files = Array.Empty<string>();
            try { files = Directory.GetFiles(dir); } catch { }
            foreach (var f in files) yield return f;
            string[] sub = Array.Empty<string>();
            try { sub = Directory.GetDirectories(dir); } catch { }
            foreach (var d in sub) stack.Push(d);
        }
    }
}