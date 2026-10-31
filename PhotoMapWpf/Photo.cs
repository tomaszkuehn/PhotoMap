namespace PhotoMapWpf;

public static class Geo {
    /// Great-circle distance in kilometers.
    public static double HaversineKm(Photo a, Photo b) => Km(a.Lat, a.Lon, b.Lat, b.Lon);

    /// Great-circle distance in kilometers for raw coordinates.
    public static double Km(double lat1, double lon1, double lat2, double lon2) {
        const double R = 6_371_000;
        double f1 = lat1 * Math.PI / 180.0, f2 = lat2 * Math.PI / 180.0;
        double df = f2 - f1;
        double dl = (lon2 - lon1) * Math.PI / 180.0;
        double h = Math.Sin(df / 2) * Math.Sin(df / 2)
                 + Math.Cos(f1) * Math.Cos(f2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return 2 * R * Math.Asin(Math.Min(1.0, Math.Sqrt(h))) / 1000.0;
    }
}

public class Photo {
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string? Taken { get; set; }
    public ushort Orientation { get; set; } = 1;

    static readonly string[] TakenFormats = {
        "yyyy:MM:dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy:MM:dd HH:mm:ss.fff",
    };

    /// Sort key for chronology lines (EXIF DateTimeOriginal ticks, else file mtime).
    public long TimeTicks { get; set; }

    public static long? TryGetTicks(string? taken) {
        if (string.IsNullOrWhiteSpace(taken)) return null;
        if (DateTime.TryParseExact(
                taken.TrimEnd('\0'),
                TakenFormats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var d)) {
            return d.Ticks;
        }
        return null;
    }
}