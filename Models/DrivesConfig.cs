namespace QuirrelBasic.Models;

public sealed class DrivesConfig
{
    public string GoogleClientSecretPath { get; set; } = "client_secret.json";
    public string DataDirectory { get; set; } = "data";
    public int IntervalSeconds { get; set; } = 60;
    public int SettleSeconds { get; set; } = 10;
    public List<SyncPair> Pairs { get; set; } = [];

    public void Validate(string directory)
    {
        if (string.IsNullOrWhiteSpace(GoogleClientSecretPath) || string.IsNullOrWhiteSpace(DataDirectory))
            throw new ArgumentException("GoogleClientSecretPath and DataDirectory are required.");
        GoogleClientSecretPath = Resolve(GoogleClientSecretPath, directory);
        DataDirectory = Resolve(DataDirectory, directory);
        if (IntervalSeconds < 5 || SettleSeconds < 0) throw new ArgumentException("Invalid synchronization interval.");
        if (Pairs == null || Pairs.Count == 0) throw new ArgumentException("Configure at least one pair.");
        foreach (var pair in Pairs)
        {
            if (pair == null || pair.RemotePath == null || pair.PauseWhileProcessesRunning == null)
                throw new ArgumentException("Pairs and their properties cannot be null.");
            if (string.IsNullOrWhiteSpace(pair.LocalPath)) throw new ArgumentException("LocalPath is required.");
            pair.LocalPath = Resolve(pair.LocalPath, directory);
            if (Inside(GoogleClientSecretPath, pair.LocalPath)) throw new ArgumentException("OAuth credentials cannot be synchronized.");
            if (pair.Kind is not ("Folder" or "File")) throw new ArgumentException("Kind must be Folder or File.");
            if (string.IsNullOrWhiteSpace(pair.GoogleRootId)) throw new ArgumentException("GoogleRootId is required.");
            pair.RemotePath = string.Join("/", pair.RemotePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
            if (pair.PauseWhileProcessesRunning.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Process names cannot be empty.");
            foreach (var part in pair.RemotePath.Split('/', StringSplitOptions.RemoveEmptyEntries)) PathSafety.Name(part);
            if (pair.Kind == "File" && string.IsNullOrWhiteSpace(pair.RemotePath)) throw new ArgumentException("File requires RemotePath.");
            if (Overlaps(pair.LocalPath, DataDirectory)) throw new ArgumentException("DataDirectory cannot overlap synchronized paths.");
        }
        for (var i = 0; i < Pairs.Count; i++)
            for (var j = i + 1; j < Pairs.Count; j++)
                if (Overlaps(Pairs[i].LocalPath, Pairs[j].LocalPath) ||
                    (Pairs[i].GoogleRootId == Pairs[j].GoogleRootId && RemoteOverlaps(Pairs[i].RemotePath, Pairs[j].RemotePath)))
                    throw new ArgumentException("Synchronization pairs cannot overlap.");
    }
    private static string Resolve(string path, string directory) => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path), directory);
    private static bool Overlaps(string a, string b) => Inside(a, b) || Inside(b, a);
    private static bool Inside(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(Path.TrimEndingDirectorySeparator(b) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool RemoteOverlaps(string a, string b) => a.Trim('/').Equals(b.Trim('/'), StringComparison.OrdinalIgnoreCase) || (a.Trim('/') + "/").StartsWith(b.Trim('/') + "/", StringComparison.OrdinalIgnoreCase) || (b.Trim('/') + "/").StartsWith(a.Trim('/') + "/", StringComparison.OrdinalIgnoreCase) || a.Trim('/') == "" || b.Trim('/') == "";
}

public sealed class SyncPair
{
    public string LocalPath { get; set; } = "";
    public string GoogleRootId { get; set; } = "root";
    public string RemotePath { get; set; } = "";
    public string Kind { get; set; } = "Folder";
    public string[] PauseWhileProcessesRunning { get; set; } = [];
}

public static class PathSafety
{
    public static void Name(string name)
    {
        if (name.StartsWith(".quirrel-", StringComparison.OrdinalIgnoreCase)) throw new IOException("The .quirrel- prefix is reserved for temporary files.");
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') ||
            name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 || name.Any(char.IsControl) ||
            stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            throw new IOException($"Unsafe Windows file name: {name}");
    }
    public static void NoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Symbolic links and junctions are not synchronized: {current}");
    }
}
