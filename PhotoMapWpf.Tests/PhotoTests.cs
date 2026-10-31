using PhotoMapWpf;
using Xunit;

namespace PhotoMapWpf.Tests;

public class PhotoTests {
    [Fact]
    public void Parses_datetimeoriginal_variants() {
        Assert.NotNull(Photo.TryGetTicks("2023:07:14 10:23:45"));
        Assert.NotNull(Photo.TryGetTicks("2023-07-14 10:23:45"));
        Assert.Null(Photo.TryGetTicks(null));
        Assert.Null(Photo.TryGetTicks(""));
        Assert.Null(Photo.TryGetTicks("gibberish"));
    }

    [Fact]
    public void Ticks_order_older_to_newer() {
        long old = Photo.TryGetTicks("2023:07:14 10:23:45")!.Value;
        long newer = Photo.TryGetTicks("2024-01-02 08:00:00")!.Value;
        Assert.True(old < newer);
    }
}