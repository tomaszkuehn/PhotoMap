namespace PhotoMapWpf;

public static class Hashing {
    public static string FnvHex(string s) {
        ulong h = 0xcbf29ce484222325UL;
        foreach (var b in EncodingUtf8(s)) {
            h ^= b;
            h *= 0x100000001b3UL;
        }
        return h.ToString("x16");
    }

    static byte[] EncodingUtf8(string s) => System.Text.Encoding.UTF8.GetBytes(s);
}