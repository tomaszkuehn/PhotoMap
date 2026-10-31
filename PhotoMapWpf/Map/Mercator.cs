namespace PhotoMapWpf.Map;

public static class Mercator {
    public const int TileSize = 256;

    public static double WorldSize(int zoom) => TileSize * Math.Pow(2, zoom);

    public static (double X, double Y) LatLonToWorld(double lat, double lon, int zoom) {
        lat = Math.Clamp(lat, -85.05112878, 85.05112878);
        double ws = WorldSize(zoom);
        double x = (lon + 180.0) / 360.0 * ws;
        double sin = Math.Sin(lat * Math.PI / 180.0);
        double y = (0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * ws;
        return (x, y);
    }

    public static (double Lat, double Lon) WorldToLatLon(double x, double y, int zoom) {
        double ws = WorldSize(zoom);
        double lon = x / ws * 360.0 - 180.0;
        double n = Math.PI * (1 - 2 * y / ws);
        double lat = 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
        return (lat, lon);
    }
}