using System.IO;
using PhotoMapWpf;
using Xunit;

namespace PhotoMapWpf.Tests;

public class TripServiceTests : IDisposable {
    readonly string _tripDir;
    static readonly Photo A = P(0, 0);
    static readonly Photo B = P(0.0045);   // ~500 m north of A
    static readonly Photo C = P(0.0090);   // ~1.4 km north of A
    static readonly Photo far = P(2);

    static Photo P(double lat, double lon = 0) {
        return new Photo {
            Name = "p" + lat.ToString("G").Replace('.', '_') + ".jpg",
            Path = @"C:\x\" + lat + ".jpg",
            Lat = lat,
            Lon = lon,
        };
    }

    public TripServiceTests() {
        _tripDir = Path.Combine(Path.GetTempPath(), "photomap-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tripDir);
        Directory.CreateDirectory(Path.Combine(_tripDir, "sub"));
    }

    public void Dispose() {
        try { Directory.Delete(_tripDir, true); } catch { }
    }

    [Fact]
    public void Trip_photos_within_1km_form_one_location() {
        var locs = TripService.Build(
            new List<Photo> { A, B }, new List<Photo>(), _tripDir, new HashSet<string>());
        Assert.Single(locs);
        Assert.Equal(2, locs[0].Travel.Count);
    }

    [Fact]
    public void Trip_photos_2km_apart_split_into_two_locations() {
        var locs = TripService.Build(
            new List<Photo> { A, far }, new List<Photo>(), _tripDir, new HashSet<string>());
        Assert.Equal(2, locs.Count);
    }

    [Fact]
    public void Candidates_only_within_1km_of_location_center() {
        var near = P(-0.002);   //  ~0.72 km south of the location center
        var mid = P(0.017);     // ~1.39 km north of the center → out
        var locs = TripService.Build(
            new List<Photo> { A, B, C },
            new List<Photo> { near, mid, far },
            _tripDir, new HashSet<string>());

        Assert.Single(locs);
        var cands = locs[0].Candidates;
        Assert.Contains(near, cands);
        Assert.DoesNotContain(mid, cands);
        Assert.DoesNotContain(far, cands);
    }

    [Fact]
    public void Excludes_trip_photos_names_already_in_folder_and_copied_paths() {
        File.Copy(typeof(TripServiceTests).Assembly.Location, Path.Combine(_tripDir, "same.jpg"));
        var catalog = new List<Photo> {
            new Photo { Name = "same.jpg", Path = @"C:\catalog\same.jpg", Lat = 0.0001, Lon = 0 },
            new Photo { Name = "copied.jpg", Path = @"C:\catalog\copied.jpg", Lat = 0.0002, Lon = 0 },
            new Photo { Name = "fresh.jpg", Path = @"C:\catalog\fresh.jpg", Lat = 0.0003, Lon = 0 },
        };
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"c:\catalog\copied.jpg" };
        var locs = TripService.Build(new List<Photo> { A }, catalog, _tripDir, copied);

        var names = locs[0].Candidates.Select(p => p.Name).ToList();
        Assert.Equal(new[] { "fresh.jpg" }, names);
    }
}