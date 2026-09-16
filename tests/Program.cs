using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using QuirrelBasic.IServices;
using QuirrelBasic.Models;
using QuirrelBasic.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Local tree uploads, empty folders created, repeat is idempotent", async () =>
    {
        using var f = new Fixture();
        Directory.CreateDirectory(Path.Combine(f.Local, "empty"));
        f.Write("save.dat", "local", -100);
        await f.Run(); Check(f.Drive.Files.Values.Any(x => x.Name == "empty" && x.IsFolder));
        Check(f.Drive.Text("save.dat") == "local");
        var count = f.Drive.Uploads; await f.Run(); Check(f.Drive.Uploads == count);
    }),
    ("Remote tree downloads and preserves timestamp", async () =>
    {
        using var f = new Fixture(); var folder = await f.Drive.CreateFolderAsync("root", "nested", default);
        var file = f.Drive.Put("save.dat", "remote", -100, folder.Id);
        await f.Run(); Check(File.ReadAllText(Path.Combine(f.Local, "nested", "save.dat")) == "remote");
        Check(File.GetLastWriteTimeUtc(Path.Combine(f.Local, "nested", "save.dat")) == file.ModifiedTime.UtcDateTime);
    }),
    ("Newer local wins and remote losing copy is backed up", async () =>
    {
        using var f = new Fixture(); f.Write("save.dat", "new", -100); f.Drive.Put("save.dat", "old", -200);
        await f.Run(); Check(f.Drive.Text("save.dat") == "new"); Check(f.Backups().Contains("old"));
    }),
    ("Newer remote wins and local losing copy is backed up", async () =>
    {
        using var f = new Fixture(); f.Write("save.dat", "old", -200); f.Drive.Put("save.dat", "new", -100);
        await f.Run(); Check(File.ReadAllText(Path.Combine(f.Local, "save.dat")) == "new"); Check(f.Backups().Contains("old"));
    }),
    ("Equal timestamps with different content preserve both copies", async () =>
    {
        using var f = new Fixture(); f.Write("save.dat", "local", -100);
        var item = f.Drive.Put("save.dat", "remote", -100);
        File.SetLastWriteTimeUtc(Path.Combine(f.Local, "save.dat"), item.ModifiedTime.UtcDateTime);
        await f.Run(); Check(f.Drive.Text("save.dat") == "local"); Check(f.Backups().Contains("remote"));
    }),
    ("Missing counterparts are restored in either direction", async () =>
    {
        using var f = new Fixture(); f.Write("save.dat", "content", -100); await f.Run();
        File.Delete(Path.Combine(f.Local, "save.dat")); await f.Run();
        Check(File.ReadAllText(Path.Combine(f.Local, "save.dat")) == "content");
        var item = f.Drive.Files.Values.Single(x => x.Name == "save.dat"); f.Drive.Files.Remove(item.Id);
        await f.Run(); Check(f.Drive.Text("save.dat") == "content");
    }),
    ("Dry run creates no local tree or remote entries", async () =>
    {
        using var f = new Fixture(); f.Config.Pairs[0].RemotePath = "new/deep";
        await f.Run(true); Check(!Directory.Exists(f.Local)); Check(f.Drive.Files.Count == 1);
    }),
    ("Single-file mapping permits different remote name", async () =>
    {
        using var f = new Fixture(); f.Write("save.dat", "content", -100);
        f.Config.Pairs[0].LocalPath = Path.Combine(f.Local, "save.dat");
        f.Config.Pairs[0].RemotePath = "games/cloud.dat"; f.Config.Pairs[0].Kind = "File";
        await f.Run(); Check(f.Drive.Text("cloud.dat") == "content");
    }),
    ("Duplicate names fail before touching local files", async () =>
    {
        using var f = new Fixture(); f.Drive.Put("save.dat", "one", -100); f.Drive.Put("SAVE.dat", "two", -100);
        await Fails(f.Run); Check(!Directory.Exists(f.Local));
    }),
    ("Unsafe remote traversal name is rejected", async () =>
    {
        using var f = new Fixture(); f.Drive.Put("../escape", "bad", -100); await Fails(f.Run);
        Check(!Directory.Exists(f.Local));
    }),
    ("Failed download leaves original intact and removes temporary file", async () =>
    {
        using var f = new Fixture(); f.Write("save.dat", "original", -200); f.Drive.Put("save.dat", "new", -100);
        f.Drive.FailDownload = true; await Fails(f.Run);
        Check(File.ReadAllText(Path.Combine(f.Local, "save.dat")) == "original");
        Check(Directory.GetFiles(f.Local, ".quirrel-*").Length == 0);
    }),
    ("Checksum mismatch prevents replacement", async () =>
    {
        using var f = new Fixture(); f.Write("save.dat", "original", -200); f.Drive.Put("save.dat", "new", -100);
        f.Drive.CorruptDownload = true; await Fails(f.Run); Check(File.ReadAllText(Path.Combine(f.Local, "save.dat")) == "original");
    }),
    ("Remote edit during download prevents replacement", async () =>
    {
        using var f = new Fixture(); f.Write("save.dat", "original", -200); f.Drive.Put("save.dat", "new", -100);
        f.Drive.MutateDownload = true; await Fails(f.Run); Check(File.ReadAllText(Path.Combine(f.Local, "save.dat")) == "original");
    }),
    ("Recent file waits for settle interval", async () =>
    {
        using var f = new Fixture(); f.Config.SettleSeconds = 60; f.Write("save.dat", "active", 0);
        await f.Run(); Check(f.Drive.Uploads == 0);
    }),
    ("Running configured process pauses pair", async () =>
    {
        using var f = new Fixture(); f.Write("save.dat", "active", -100);
        f.Config.Pairs[0].PauseWhileProcessesRunning = [System.Diagnostics.Process.GetCurrentProcess().ProcessName];
        await f.Run(); Check(f.Drive.Uploads == 0);
    }),
    ("Locked local file cannot be overwritten", async () =>
    {
        using var f = new Fixture(); f.Write("save.dat", "original", -200); f.Drive.Put("save.dat", "new", -100);
        using var locked = new FileStream(Path.Combine(f.Local, "save.dat"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Fails(f.Run); Check(f.Drive.Uploads == 0);
    }),
    ("Listing failure never becomes an empty remote listing", async () =>
    {
        using var f = new Fixture(); f.Write("save.dat", "content", -100); f.Drive.FailList = true;
        await Fails(f.Run); Check(f.Drive.Uploads == 0);
    }),
    ("Unrelated root items do not block resolving configured path", async () =>
    {
        using var f = new Fixture(); f.Drive.Put("invalid:name", "unrelated", -100);
        f.Config.Pairs[0].RemotePath = "games"; f.Write("save.dat", "content", -100);
        await f.Run(); Check(f.Drive.Text("save.dat") == "content");
    }),
    ("Unsupported Google document is not downloaded", async () =>
    {
        using var f = new Fixture(); var item = f.Drive.Put("document", "native", -100);
        f.Drive.Files[item.Id] = item with { IsSupported = false };
        await Fails(f.Run); Check(!File.Exists(Path.Combine(f.Local, "document")));
    }),
    ("Configuration rejects slash-only single-file remote path", () =>
    {
        using var f = new Fixture(); f.Config.Pairs[0].Kind = "File"; f.Config.Pairs[0].RemotePath = "///";
        try { f.Config.Validate(f.Root); throw new Exception("Expected validation error"); } catch (ArgumentException) { }
        return Task.CompletedTask;
    }),
    ("Configuration rejects overlapping remote paths with repeated separators", () =>
    {
        using var f = new Fixture(); f.Config.Pairs[0].RemotePath = "games//world";
        f.Config.Pairs.Add(new SyncPair { LocalPath = Path.Combine(f.Root, "second"), RemotePath = "games/world/child" });
        try { f.Config.Validate(f.Root); throw new Exception("Expected validation error"); } catch (ArgumentException) { }
        return Task.CompletedTask;
    }),
    ("Configuration rejects overlapping local paths", () =>
    {
        using var f = new Fixture(); f.Config.Pairs.Add(new SyncPair { LocalPath = Path.Combine(f.Local, "child"), RemotePath = "other" });
        try { f.Config.Validate(f.Root); throw new Exception("Expected validation error"); } catch (ArgumentException) { }
        return Task.CompletedTask;
    })
};
var failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
static async Task Fails(Func<bool, Task> run)
{
    try { await run(false); } catch (AggregateException) { return; }
    throw new Exception("Expected synchronization error");
}

sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "quirrel-tests-" + Guid.NewGuid());
    public string Local => Path.Combine(Root, "local");
    public FakeDrive Drive { get; } = new();
    public DrivesConfig Config { get; }
    public Fixture() { Directory.CreateDirectory(Root); Config = new() { DataDirectory = Path.Combine(Root, "data"), SettleSeconds = 0, Pairs = [new() { LocalPath = Local }] }; }
    public void Write(string name, string text, int seconds) { Directory.CreateDirectory(Local); File.WriteAllText(Path.Combine(Local, name), text); File.SetLastWriteTimeUtc(Path.Combine(Local, name), DateTime.UtcNow.AddSeconds(seconds)); }
    public Task Run(bool dry = false) => new SyncEngine(Drive, Config, NullLogger<SyncEngine>.Instance).RunAsync(dry, default);
    public string[] Backups() => Directory.GetFiles(Path.Combine(Config.DataDirectory, "backups"), "*", SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();
    public void Dispose() { Directory.Delete(Root, true); }
}

sealed class FakeDrive : IDriveService
{
    public Dictionary<string, DriveFile> Files { get; } = new() { ["root"] = new("root", "root", true, DateTimeOffset.MinValue, null, null) };
    private readonly Dictionary<string, byte[]> content = [];
    private readonly Dictionary<string, string> parents = [];
    public int Uploads;
    public bool FailDownload, CorruptDownload, MutateDownload, FailList;
    public DriveFile Put(string name, string text, int seconds, string parent = "root")
    {
        var bytes = Encoding.UTF8.GetBytes(text); var id = Guid.NewGuid().ToString();
        var item = new DriveFile(id, name, false, DateTimeOffset.UtcNow.AddSeconds(seconds), Convert.ToHexString(MD5.HashData(bytes)), bytes.Length);
        Files[id] = item; content[id] = bytes; parents[id] = parent; return item;
    }
    public string Text(string name) => Encoding.UTF8.GetString(content[Files.Values.Single(x => x.Name == name).Id]);
    public Task<DriveFile> GetAsync(string id, CancellationToken token) => Task.FromResult(Files[id]);
    public Task<IReadOnlyList<DriveFile>> ListAsync(string parent, CancellationToken token)
    {
        if (FailList) throw new IOException("offline");
        return Task.FromResult<IReadOnlyList<DriveFile>>(Files.Values.Where(x => parents.GetValueOrDefault(x.Id) == parent).ToArray());
    }
    public Task<DriveFile> CreateFolderAsync(string parent, string name, CancellationToken token)
    {
        var item = new DriveFile(Guid.NewGuid().ToString(), name, true, DateTimeOffset.UtcNow, null, null);
        Files[item.Id] = item; parents[item.Id] = parent; return Task.FromResult(item);
    }
    public async Task DownloadAsync(string id, Stream target, CancellationToken token)
    {
        await target.WriteAsync(CorruptDownload ? Encoding.UTF8.GetBytes("bad") : content[id], token);
        if (FailDownload) throw new IOException("network interrupted");
        if (MutateDownload) Files[id] = Files[id] with { ModifiedTime = DateTimeOffset.UtcNow };
    }
    public async Task UploadAsync(string parent, string name, string? id, Stream source, DateTimeOffset modified, CancellationToken token)
    {
        using var buffer = new MemoryStream(); await source.CopyToAsync(buffer, token);
        var bytes = buffer.ToArray(); id ??= Guid.NewGuid().ToString();
        Files[id] = new(id, name, false, modified, Convert.ToHexString(MD5.HashData(bytes)), bytes.Length);
        content[id] = bytes; parents[id] = parent; Uploads++;
    }
}
