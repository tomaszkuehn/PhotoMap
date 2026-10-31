using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PhotoMapWpf.Map;

sealed class TileLoader : IDisposable {
    static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoMap", "tiles");

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    readonly SemaphoreSlim _gate = new(8, 8);

    public TileLoader() {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PhotoMap/1.0 (Windows desktop)");
    }

    public void LoadAsync(int z, int x, int y, Image target, string key) {
        _ = Task.Run(async () => {
            byte[]? bytes = null;
            try {
                bytes = await GetTileBytesAsync(z, x, y).ConfigureAwait(false);
            } catch { }
            if (bytes == null) return;
            var app = Application.Current;
            if (app == null) return;
            _ = app.Dispatcher.BeginInvoke(() => {
                if (ReferenceEquals(target.Tag, key) && target.Parent != null) {
                    target.Source = Decode(bytes);
                }
            });
        });
    }

    async Task<byte[]?> GetTileBytesAsync(int z, int x, int y) {
        var file = Path.Combine(CacheRoot, z.ToString(), x.ToString(), y + ".png");
        if (File.Exists(file)) {
            try {
                return await File.ReadAllBytesAsync(file).ConfigureAwait(false);
            } catch { }
        }
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        } catch { }
        await _gate.WaitAsync().ConfigureAwait(false);
        try {
            var bytes = await _http
                .GetByteArrayAsync($"https://tile.openstreetmap.org/{z}/{x}/{y}.png")
                .ConfigureAwait(false);
            try { File.WriteAllBytes(file, bytes); } catch { }
            return bytes;
        } catch {
            return null;
        } finally {
            _gate.Release();
        }
    }

    internal static BitmapImage Decode(byte[] bytes) {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = new MemoryStream(bytes);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    public void Dispose() {
        _http.Dispose();
        _gate.Dispose();
    }
}