using System.IO;
using System.Text;
using PhotoMapWpf;
using Xunit;
namespace PhotoMapWpf.Tests;

public class ExifParserTests {
    /// Builds a synthetic minimal JPEG with an EXIF APP1 segment (GPS + DateTimeOriginal + Orientation).
    static byte[] BuildJpeg(
        bool be = false,
        bool withGps = true,
        char latRef = 'N',
        char lonRef = 'E',
        uint la0 = 40, uint la1 = 15, uint la2n = 0,
        uint lo0 = 30, uint lo1 = 20, uint lo2n = 0) {

        int ifd0Entries = withGps ? 3 : 1;
        const int ifd0Off = 8;
        int ifd0Size = 2 + ifd0Entries * 12 + 4;
        int gpsOff = ifd0Off + ifd0Size;
        int gpsSize = withGps ? 2 + 4 * 12 + 4 : 0;
        int exifOff = gpsOff + gpsSize;
        int exifIfdSize = 2 + 12 + 4;
        int dataOff = exifOff + exifIfdSize;
        int latOff = dataOff;
        int lonOff = dataOff + 24;
        int dateOff = dataOff + 48;

        using var tiff = new MemoryStream();
        using (var w = new BinaryWriter(tiff, Encoding.UTF8, leaveOpen: true)) {
            void W16(ushort v) {
                if (be) {
                    w.Write((byte)(v >> 8));
                    w.Write((byte)(v & 0xFF));
                } else {
                    w.Write((byte)(v & 0xFF));
                    w.Write((byte)(v >> 8));
                }
            }
            void W32(uint v) {
                if (be) {
                    w.Write((byte)(v >> 24));
                    w.Write((byte)((v >> 16) & 0xFF));
                    w.Write((byte)((v >> 8) & 0xFF));
                    w.Write((byte)(v & 0xFF));
                } else {
                    w.Write((byte)(v & 0xFF));
                    w.Write((byte)((v >> 8) & 0xFF));
                    w.Write((byte)((v >> 16) & 0xFF));
                    w.Write((byte)(v >> 24));
                }
            }
            void Entry(ushort tag, ushort type, uint count) {
                W16(tag);
                W16(type);
                W32(count);
            }
            void InlineU16(ushort v) { W16(v); W16(0); }
            void InlineU32(uint v) { W32(v); }
            void Date(long at) {
                var pos = tiff.Position;
                tiff.Position = at;
                var bytes = Encoding.ASCII.GetBytes("2023:07:14 10:23:45\0");
                Assert.Equal(20, bytes.Length);
                w.Write(bytes);
                tiff.Position = pos;
            }

            // TIFF header
            w.Write((byte)(be ? 0x4D : 0x49));
            w.Write((byte)(be ? 0x4D : 0x49));
            W16(42);
            W32(ifd0Off);

            // IFD0: Orientation(0x0112), ExifIFD ptr(0x8769), GPS IFD ptr(0x8825)
            W16((ushort)ifd0Entries);
            Entry(0x0112, 3, 1);
            InlineU16(6);                       // orientation = rotate 90 CW
            if (withGps) {
                Entry(0x8769, 4, 1);
                InlineU32((uint)exifOff);
                Entry(0x8825, 4, 1);
                InlineU32((uint)gpsOff);
            }
            W32(0);                             // next IFD

            if (withGps) {
                W16(4);
                Entry(1, 2, 2);                 // GPSLatitudeRef, ASCII inline
                w.Write((byte)latRef); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                Entry(2, 5, 3);                 // GPSLatitude, RATIONALE x3
                InlineU32((uint)latOff);
                Entry(3, 2, 2);                 // GPSLongitudeRef
                w.Write((byte)lonRef); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                Entry(4, 5, 3);                 // GPSLongitude
                InlineU32((uint)lonOff);
                W32(0);

                W16(1);
                Entry(0x9003, 2, 20);           // DateTimeOriginal
                InlineU32((uint)dateOff);
                W32(0);

                // data area
                tiff.Position = latOff;
                W32(la0); W32(1); W32(la1); W32(1); W32(la2n); W32(1);
                W32(lo0); W32(1); W32(lo1); W32(1); W32(lo2n); W32(1);
                Date(dateOff);
            }
        }

        // JPEG assembly
        var ms = new MemoryStream();
        ms.Write(new byte[] { 0xFF, 0xD8 });
        var payload = tiff.ToArray();
        ms.Write(new byte[] { 0xFF, 0xE1 });
        ms.Write(new byte[] { (byte)((payload.Length + 2) >> 8), (byte)((payload.Length + 2) & 0xFF) });
        ms.Write(Encoding.ASCII.GetBytes("Exif\0\0"));
        ms.Write(payload);
        ms.Write(new byte[] { 0xFF, 0xD9 });
        return ms.ToArray();
    }

    const double La = 40.0 + 15.0 / 60.0;          // 40.25
    const double Lo = 30.0 + 20.0 / 60.0;          // 30.3333…

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parses_gps_date_orientation(bool be) {
        var r = ExifParser.Parse(new MemoryStream(BuildJpeg(be: be)));
        Assert.NotNull(r);
        Assert.Equal(La, r!.Lat, 5);
        Assert.Equal(Lo, r.Lon, 5);
        Assert.Equal("2023:07:14 10:23:45", r.Taken);
        Assert.Equal((ushort)6, r.Orientation);
    }

    [Fact]
    public void Applies_south_and_west_references() {
        var r = ExifParser.Parse(new MemoryStream(BuildJpeg(latRef: 'S', lonRef: 'W')));
        Assert.NotNull(r);
        Assert.Equal(-La, r!.Lat, 5);
        Assert.Equal(-Lo, r.Lon, 5);
    }

    [Fact]
    public void Rejects_zero_zero_coordinates() {
        var r = ExifParser.Parse(new MemoryStream(BuildJpeg(la0: 0, la1: 0, lo0: 0, lo1: 0)));
        Assert.Null(r);
    }

    [Fact]
    public void Rejects_jpeg_without_exif() {
        var jpeg = new MemoryStream();
        jpeg.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        Assert.Null(ExifParser.Parse(jpeg));
    }

    [Fact]
    public void Rejects_non_jpeg_bytes() {
        var junk = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        Assert.Null(ExifParser.Parse(junk));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rejects_jpeg_without_gps_ifd(bool be) {
        var r = ExifParser.Parse(new MemoryStream(BuildJpeg(be: be, withGps: false)));
        Assert.Null(r);
    }
}