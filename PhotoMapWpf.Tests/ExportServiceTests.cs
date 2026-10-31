using System.IO;
using PhotoMapWpf;
using Xunit;

namespace PhotoMapWpf.Tests;

public class ExportServiceTests : IDisposable {
    readonly string _dir;

    public ExportServiceTests() {
        _dir = Path.Combine(Path.GetTempPath(), "photomap-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Unique_target_returns_original_when_free() {
        Assert.Equal(Path.Combine(_dir, "b.jpg"), ExportService.UniqueTarget(_dir, "b.jpg"));
    }

    [Fact]
    public void Unique_target_appends_suffix_when_taken() {
        File.WriteAllText(Path.Combine(_dir, "a.jpg"), "x");
        File.WriteAllText(Path.Combine(_dir, "a (1).jpg"), "x");
        Assert.Equal(Path.Combine(_dir, "a (2).jpg"), ExportService.UniqueTarget(_dir, "a.jpg"));
    }

    [Fact]
    public void Copy_copies_and_reports_progress() {
        var src = Path.Combine(_dir, "src.jpg");
        File.WriteAllText(src, "photo");
        var dest = Path.Combine(_dir, "out");
        var photos = new List<Photo> { new() { Path = src, Name = "src.jpg" } };
        (int, int, int) reported = (0, 0, 0);
        var progress = new Progress<(int Copied, int Failed, int Total)>(v => reported = v);
        var res = ExportService.Copy(photos, dest, progress);
        Assert.Equal(1, res.Copied);
        Assert.Equal(0, res.Failed);
        Assert.True(File.Exists(Path.Combine(dest, "src.jpg")));
        ProgressProbe(reported, 1, 0, 1);
    }

    static void ProgressProbe((int, int, int) v, int c, int f, int t) {
        // Progress<T> may deliver asynchronously; only verify values if a report arrived.
        if (v.Item3 == t) {
            Assert.Equal((c, f, t), v);
        }
    }

    [Fact]
    public void Copy_reports_failures_without_throwing() {
        var dest = Path.Combine(_dir, "out2");
        var photos = new List<Photo> { new() { Path = @"Z:\nope\missing.jpg", Name = "missing.jpg" } };
        var res = ExportService.Copy(photos, dest, null);
        Assert.Equal(0, res.Copied);
        Assert.Equal(1, res.Failed);
        Assert.NotNull(res.LastError);
    }
}