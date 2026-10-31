using PhotoMapWpf;
using PhotoMapWpf.Map;
using Xunit;

namespace PhotoMapWpf.Tests;

public class MercatorTests {
    [Fact]
    public void Haversine_known_distances() {
        var warsaw = new Photo { Lat = 52.2297, Lon = 21.0122 };
        var krakow = new Photo { Lat = 50.0647, Lon = 19.9450 };
        double d = Geo.HaversineKm(warsaw, krakow);
        Assert.InRange(d, 240, 266);
        Assert.Equal(0, Geo.HaversineKm(warsaw, warsaw), 5);
    }
    [Fact]
    public void Origin_of_web_mercator_is_world_center() {
        var (lon, _) = (0.0, 0.0);
        var (x, y) = Mercator.LatLonToWorld(0, lon, 0);
        Assert.Equal(128, x, 5);
        Assert.Equal(128, y, 5);
    }

    [Fact]
    public void Round_trip_preserves_coordinates() {
        foreach (var (lat, lon) in new[] {
            (52.2297, 21.0122), (-41.88972, 175.2), (0, 0), (85.0, -179.9), (-85.0, 179.9)
        }) {
            for (int z = 2; z <= 19; z++) {
                var (x, y) = Mercator.LatLonToWorld(lat, lon, z);
                var (lat2, lon2) = Mercator.WorldToLatLon(x, y, z);
                Assert.Equal(lat, lat2, 4);
                Assert.Equal(lon, lon2, 4);
            }
        }
    }

    [Fact]
    public void World_size_doubles_per_zoom() {
        Assert.Equal(256 * 4, Mercator.WorldSize(2), 5);
        Assert.Equal(256 * 2048, Mercator.WorldSize(11), 5);
    }
}