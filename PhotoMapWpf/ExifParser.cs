using System.IO;
using System.Text;

namespace PhotoMapWpf;

/// Minimal JPEG/EXIF reader: GPS coordinates, capture date, orientation. No external dependencies.
public static class ExifParser {
    public sealed record Exif(double Lat, double Lon, string? Taken, ushort Orientation);

    const int ReadChunk = 262144;

    public static Exif? Parse(string path) {
        try {
            using var fs = File.OpenRead(path);
            return Parse(fs);
        } catch {
            return null;
        }
    }

    public static Exif? Parse(Stream stream) {
        try {
            var buf = new byte[ReadChunk];
            int total = Fill(stream, buf);
            if (total < 4 || buf[0] != 0xFF || buf[1] != 0xD8) return null;

            int i = 2;
            while (i + 4 <= total) {
                if (buf[i] != 0xFF) return null;
                int marker = buf[i + 1];
                if (marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01) {
                    i += 2;
                    continue;
                }
                if (marker == 0xDA) return null; // start of image scan, EXIF absent
                int len = (buf[i + 2] << 8) | buf[i + 3];
                if (len < 2) return null;
                if (marker == 0xE1 && HasPrefix(buf, i + 4, total, "Exif")) {
                    return ParseTiff(buf, total, i + 4 + 6);
                }
                i += 2 + len;
            }
            return null;
        } catch {
            return null;
        }
    }

    static int Fill(Stream s, byte[] buf) {
        int read = 0, n;
        while (read < buf.Length && (n = s.Read(buf, read, buf.Length - read)) > 0) {
            read += n;
        }
        return read;
    }

    static bool HasPrefix(byte[] b, int off, int total, string s) {
        if (off + s.Length > total) return false;
        for (int j = 0; j < s.Length; j++) {
            if (b[off + j] != s[j]) return false;
        }
        return true;
    }

    static bool U16(byte[] b, long off, bool be, out ushort v) {
        v = 0;
        if (off < 0 || off + 2 > b.Length) return false;
        v = be
            ? (ushort)((b[off] << 8) | b[off + 1])
            : (ushort)(b[off] | (b[off + 1] << 8));
        return true;
    }

    static bool U32(byte[] b, long off, bool be, out uint v) {
        v = 0;
        if (off < 0 || off + 4 > b.Length) return false;
        if (be) {
            v = (uint)(b[off] << 24 | b[off + 1] << 16 | b[off + 2] << 8 | b[off + 3]);
        } else {
            v = (uint)(b[off] | b[off + 1] << 8 | b[off + 2] << 16 | b[off + 3] << 24);
        }
        return true;
    }

    static Exif? ParseTiff(byte[] b, int total, int t) {
        if (t + 8 > total) return null;
        bool le = b[t] == 0x49 && b[t + 1] == 0x49;   // "II"
        bool be = b[t] == 0x4D && b[t + 1] == 0x4D;   // "MM"
        if (!le && !be) return null;
        if (!U16(b, t + 2, be, out var magic) || magic != 42) return null;
        if (!U32(b, t + 4, be, out var ifd0Rel)) return null;
        long ifd0 = t + ifd0Rel;
        if (!U16(b, ifd0, be, out var n0)) return null;
        long e0 = ifd0 + 2;

        ushort orientation = 1;
        long gpsRel = -1, exifRel = -1;
        for (int k = 0; k < n0; k++) {
            long eo = e0 + (long)k * 12;
            if (eo + 12 > total) break;
            if (!U16(b, eo, be, out var tag)) break;
            if (tag == 0x0112) {
                if (U16(b, eo + 8, be, out var o) && o is >= 1 and <= 8) orientation = o;
            } else if (tag == 0x8825) {
                if (U32(b, eo + 8, be, out var g)) gpsRel = g;
            } else if (tag == 0x8769) {
                if (U32(b, eo + 8, be, out var x)) exifRel = x;
            }
        }

        string? taken = exifRel >= 0 ? FindAscii(b, total, t, t + exifRel, 0x9003, be) : null;
        taken ??= FindAscii(b, total, t, ifd0, 0x0132, be);

        if (gpsRel < 0) return null;
        long gpsIfd = t + gpsRel;
        if (!U16(b, gpsIfd, be, out var gn)) return null;

        double? lat = null, lon = null;
        string? latRef = null, lonRef = null;
        for (int k = 0; k < gn; k++) {
            long eo = gpsIfd + 2 + (long)k * 12;
            if (eo + 12 > total) break;
            if (!U16(b, eo, be, out var tag)) break;
            switch (tag) {
                case 1: latRef = AsciiValue(b, total, t, eo, be); break;
                case 2: lat = DmsValue(b, total, t, eo, be); break;
                case 3: lonRef = AsciiValue(b, total, t, eo, be); break;
                case 4: lon = DmsValue(b, total, t, eo, be); break;
            }
        }

        if (lat is not { } la || lon is not { } lo) return null;
        if (latRef == "S") la = -la;
        else if (latRef == "N" || latRef == null) { }
        if (lonRef == "W") lo = -lo;
        if (la == 0 && lo == 0) return null; // (0,0) ≈ no GPS fix
        return new Exif(la, lo, taken, orientation);
    }

    static double? DmsValue(byte[] b, int total, int t, long eo, bool be) {
        if (!U16(b, eo + 2, be, out var type) || type != 5) return null; // RATIONAL
        if (!U32(b, eo + 4, be, out var cnt) || cnt == 0) return null;
        long vo;
        if (cnt * 8 <= 4) {
            vo = eo + 8;
        } else {
            if (!U32(b, eo + 8, be, out var rel)) return null;
            vo = t + rel;
        }
        double d = 0, m = 0, s = 0;
        for (uint j = 0; j < cnt && j < 3; j++) {
            if (!U32(b, vo + j * 8, be, out var num)) return null;
            if (!U32(b, vo + j * 8 + 4, be, out var den) || den == 0) return null;
            double val = (double)num / den;
            switch (j) {
                case 0: d = val; break;
                case 1: m = val; break;
                case 2: s = val; break;
            }
        }
        return d + m / 60.0 + s / 3600.0;
    }

    static string? AsciiValue(byte[] b, int total, int t, long eo, bool be) {
        if (!U16(b, eo + 2, be, out var type) || type != 2) return null; // ASCII
        if (!U32(b, eo + 4, be, out var cnt)) return null;
        long vo;
        if (cnt <= 4) {
            vo = eo + 8;
        } else {
            if (!U32(b, eo + 8, be, out var rel)) return null;
            vo = t + rel;
        }
        var sb = new StringBuilder();
        for (uint j = 0; j < cnt; j++) {
            long off = vo + j;
            if (off >= b.Length || off >= total) break;
            char c = (char)b[off];
            if (c == '\0') break;
            sb.Append(c);
        }
        var str = sb.ToString().Trim();
        return str.Length == 0 ? null : str;
    }

    static string? FindAscii(byte[] b, int total, int t, long ifdAbs, ushort wanted, bool be) {
        if (!U16(b, ifdAbs, be, out var n)) return null;
        long e = ifdAbs + 2;
        for (int k = 0; k < n; k++) {
            long eo = e + (long)k * 12;
            if (eo + 12 > total) break;
            if (!U16(b, eo, be, out var tag)) break;
            if (tag != wanted) continue;
            return AsciiValue(b, total, t, eo, be);
        }
        return null;
    }
}