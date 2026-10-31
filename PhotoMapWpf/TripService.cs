using System.IO;

namespace PhotoMapWpf;

/// One trip location: clusters the trip photos within <c>radiusKm</c> of each other
/// (their mean position) plus the catalog photos proposed for it.
public sealed class TripLocation {
    public double Lat;
    public double Lon;
    public long MeanTicks;
    public readonly List<Photo> Travel = new();
    public readonly List<Photo> Candidates = new();
}

public static class TripService {
    /// Greedy 1-pass clustering: a trip photo joins the first location whose mean
    /// position is within <paramref name="radiusKm"/>; otherwise it opens a new one.
    /// Candidates = catalog photos within the same radius, excluding trip photos,
    /// files already present (same file name) in the trip folder and photos already
    /// copied this session (<paramref name="copied"/>).
    public static List<TripLocation> Build(
        List<Photo> trip,
        List<Photo> catalog,
        string tripFolder,
        ISet<string>? copied,
        double radiusKm = 1.0) {

        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            foreach (var f in Directory.GetFiles(tripFolder, "*", SearchOption.AllDirectories))
                existingNames.Add(Path.GetFileName(f));
        } catch { }

        var tripPaths = new HashSet<string>(trip.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);
        var byTime = trip.OrderBy(p => p.TimeTicks).ToList();

        var locs = new List<TripLocation>();
        foreach (var p in byTime) {
            TripLocation? hit = locs.FirstOrDefault(l => Geo.Km(l.Lat, l.Lon, p.Lat, p.Lon) <= radiusKm);
            if (hit == null) {
                hit = new TripLocation();
                locs.Add(hit);
            }
            hit.Travel.Add(p);
            double sl = 0, so = 0, st = 0;
            foreach (var t in hit.Travel) { sl += t.Lat; so += t.Lon; st += t.TimeTicks; }
            int n = hit.Travel.Count;
            hit.Lat = sl / n;
            hit.Lon = so / n;
            hit.MeanTicks = (long)(st / n);
        }

        var result = new List<TripLocation>();
        foreach (var l in locs.OrderBy(l => l.MeanTicks)) {
            l.Candidates.AddRange(catalog
                .Where(p => !tripPaths.Contains(p.Path)
                         && !existingNames.Contains(p.Name)
                         && (copied == null || !copied.Contains(p.Path)))
                .Select(p => (p, d: Geo.Km(l.Lat, l.Lon, p.Lat, p.Lon)))
                .Where(t => t.d <= radiusKm)
                .OrderBy(t => t.d)
                .Select(t => t.p));
            result.Add(l);
        }
        return result;
    }
}