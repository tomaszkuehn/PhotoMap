using System.IO;

namespace PhotoMapWpf;

public record ExportOutcome(int Copied, int Failed, string? LastError);

public static class ExportService {
    public static ExportOutcome Copy(
        IEnumerable<Photo> photos,
        string destDir,
        IProgress<(int Copied, int Failed, int Total)>? progress) {

        Directory.CreateDirectory(destDir);
        var list = photos.ToList();
        int copied = 0, failed = 0;
        string? lastError = null;
        foreach (var p in list) {
            try {
                var target = UniqueTarget(destDir, Path.GetFileName(p.Path));
                File.Copy(p.Path, target, overwrite: false);
                copied++;
            } catch (Exception ex) {
                failed++;
                lastError = Path.GetFileName(p.Path) + ": " + ex.Message;
            }
            progress?.Report((copied, failed, list.Count));
        }
        return new ExportOutcome(copied, failed, lastError);
    }

    public static string UniqueTarget(string dir, string fileName) {
        var target = Path.Combine(dir, fileName);
        if (!File.Exists(target)) return target;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int k = 1; k < 1000; k++) {
            target = Path.Combine(dir, $"{stem} ({k}){ext}");
            if (!File.Exists(target)) return target;
        }
        return Path.Combine(dir, $"{stem} ({Guid.NewGuid():N}){ext}");
    }
}