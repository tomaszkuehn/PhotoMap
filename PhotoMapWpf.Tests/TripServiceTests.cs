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

    static Photo P(double lat, double lon = 0, long? ticks = null) {
        return new Photo {
            Name = "p" + lat.ToString("G").Replace('.', '_') + ".jpg",
            Path = @"C:\x\" + lat + ".jpg",
            Lat = lat,
            Lon = lon,
            TimeTicks = ticks ?? 0,
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

        var names = locs[0].Candidates.Select(n => n.Name).ToList();
        Assert.Equal(new[] { "fresh.jpg" }, names);
    }

    [Fact]
    public void Other_sections_covers_trip_period_plus_two_days_outside_locations() {
        long t0 = new DateTime(2026, 6, 10, 12, 0, 0).Ticks;
        var hour = TimeSpan.FromHours(1).Ticks;
        var trip = new List<Photo> { P(0, 0, t0), P(0.0045, 0, t0 + 8 * hour), P(0.0090, 0, t0 + 16 * hour) };

        var inRangeFar = P(5.0, 0, t0 + 20 * hour);        // ~555 km away → fits
        var outOfRange = P(5.5, 0, t0 + 4 * TimeSpan.FromDays(1).Ticks);  // >2 days after → out
        var nearLoc = P(0.004, 0, t0 + 6 * hour);          // ~55 m from the location center → out
        var tripCopy = new Photo { Name = "p0.jpg", Path = @"C:\x\0.jpg", Lat = 0, Lon = 0, TimeTicks = t0 + 2 * hour };

        var locs = TripService.Build(trip, new List<Photo>(), _tripDir, new HashSet<string>());
        var others = TripService.BuildOther(trip, new List<Photo> { inRangeFar, outOfRange, nearLoc, tripCopy },
                                            _tripDir, new HashSet<string>(), locs);

        Assert.Equal(new[] { inRangeFar }, others);
        // window is exactly ±2 days around the trip's photo dates
        var edge = P(9.0, 0, t0 - 2 * TimeSpan.FromDays(1).Ticks);
        var justOutside = P(9.5, 0, t0 - 2 * TimeSpan.FromDays(1).Ticks - 1);
        var others2 = TripService.BuildOther(trip, new List<Photo> { edge, justOutside }, _tripDir, null, locs);
        Assert.Equal(new[] { edge }, others2);
    }

    [Fact]
    public void Other_section_empty_without_trip_dates() {
        var locs = TripService.Build(new List<Photo> { A }, new List<Photo>(), _tripDir, new HashSet<string>());
        var others = TripService.BuildOther(new List<Photo> { A }, new List<Photo> { far }, _tripDir, null, locs);
        Assert.Empty(others);
    }
}