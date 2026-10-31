using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoMapWpf;

public static class ThumbnailService {
    const int Thumb = 340;

    static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoMap", "thumbs");
    static readonly ConcurrentDictionary<string, BitmapSource> Mem = new();

    public static void LoadInto(Photo p, Image target) {
        Task.Run(() => {
            var src = GetThumb(p);
            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(() => {
                if (ReferenceEquals(target.Tag, p) && src != null) target.Source = src;
            });
        });
    }

    static BitmapSource? GetThumb(Photo p) {
        if (Mem.TryGetValue(p.Path, out var m)) return m;
        var src = TryLoadFile(CachePath(p)) ?? Generate(p);
        if (src == null) return null;
        if (Mem.Count > 4000) Mem.Clear();
        Mem[p.Path] = src;
        return src;
    }

    static BitmapSource? TryLoadFile(string file) {
        try {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(file);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        } catch {
            return null;
        }
    }

    static BitmapSource? Generate(Photo p) {
        try {
            var raw = DecodeRaw(p.Path, Thumb);
            var oriented = ApplyOrientation(raw, p.Orientation);
            oriented = Downscale(oriented, Thumb);
            try {
                var file = CachePath(p);
                var dir = Path.GetDirectoryName(file);
                if (dir != null) Directory.CreateDirectory(dir);
                var enc = new JpegBitmapEncoder { QualityLevel = 82 };
                using var ms = new MemoryStream();
                enc.Frames.Add(BitmapFrame.Create(oriented));
                enc.Save(ms);
                File.WriteAllBytes(file, ms.ToArray());
            } catch { }
            return oriented;
        } catch {
            return null;
        }
    }

    internal static BitmapImage DecodeRaw(string path, int decodeWidth) {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.UriSource = new Uri(path);
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.DecodePixelWidth = decodeWidth;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    internal static BitmapSource ApplyOrientation(BitmapSource src, ushort o) {
        var s = src;
        switch (o) {
            case 2: s = FlipX(s); break;
            case 3: s = Rot(s, 180); break;
            case 4: s = FlipY(s); break;
            case 5: s = FlipX(Rot(s, 90)); break;
            case 6: s = Rot(s, 90); break;
            case 7: s = FlipY(Rot(s, 90)); break;
            case 8: s = Rot(s, 270); break;
        }
        return s;
    }

    static BitmapSource Rot(BitmapSource s, double deg) {
        var tb = new TransformedBitmap(s, new RotateTransform(deg));
        tb.Freeze();
        return tb;
    }

    static BitmapSource FlipX(BitmapSource s) {
        var tb = new TransformedBitmap(s, new ScaleTransform(-1, 1));
        tb.Freeze();
        return tb;
    }

    static BitmapSource FlipY(BitmapSource s) {
        var tb = new TransformedBitmap(s, new ScaleTransform(1, -1));
        tb.Freeze();
        return tb;
    }

    static BitmapSource Downscale(BitmapSource src, int max) {
        int m = Math.Max(src.PixelWidth, src.PixelHeight);
        if (m <= max) return src;
        double f = (double)max / m;
        var tb = new TransformedBitmap(src, new ScaleTransform(f, f));
        tb.Freeze();
        return tb;
    }

    public static Task<BitmapSource?> LoadPreviewAsync(string path, ushort orientation, int maxWidth) {
        if (maxWidth is < 400) maxWidth = 400;
        if (maxWidth > 1920) maxWidth = 1920;
        return Task.Run(() => {
            try {
                var raw = DecodeRaw(path, maxWidth);
                return (BitmapSource?)ApplyOrientation(raw, orientation);
            } catch {
                return (BitmapSource?)null;
            }
        });
    }

    static string CachePath(Photo p) {
        string size = "0", mtime = "0";
        try {
            var fi = new FileInfo(p.Path);
            if (fi.Exists) {
                size = fi.Length.ToString();
                mtime = fi.LastWriteTimeUtc.Ticks.ToString();
            }
        } catch { }
        return Path.Combine(CacheDir, $"t{Hashing.FnvHex(p.Path + "|" + size + "|" + mtime)}.jpg");
    }
}