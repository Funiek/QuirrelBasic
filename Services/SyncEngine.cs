using System.Diagnostics;
using System.Security.Cryptography;
using QuirrelBasic.IServices;
using QuirrelBasic.Models;

namespace QuirrelBasic.Services;

public sealed class SyncEngine(IDriveService drive, DrivesConfig config, ILogger<SyncEngine> logger)
{
    public async Task RunAsync(bool dryRun, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var pair in config.Pairs)
        {
            try
            {
                if (IsPaused(pair)) { logger.LogInformation("Paused: {Path}", pair.LocalPath); continue; }
                PathSafety.NoLinks(pair.LocalPath);
                var root = await drive.GetAsync(pair.GoogleRootId, token);
                if (!root.IsFolder) throw new IOException("GoogleRootId must identify a folder.");
                var parts = pair.RemotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var folders = pair.Kind == "File" ? parts[..^1] : parts;
                string? parent = root.Id;
                foreach (var name in folders) parent = await FolderAsync(parent, name, dryRun, token);
                if (pair.Kind == "Folder")
                    await DirectoryAsync(pair, pair.LocalPath, parent, dryRun, token);
                else
                {
                    var entries = await EntriesAsync(parent, token, parts[^1]);
                    await FileAsync(pair, pair.LocalPath, parent, parts[^1], entries.GetValueOrDefault(parts[^1]), dryRun, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogError(ex, "Pair failed: {Path}", pair.LocalPath); failures.Add(ex); }
        }
        if (failures.Count > 0) throw new AggregateException("Some synchronization pairs failed; retry next cycle.", failures);
    }

    private static bool IsPaused(SyncPair pair)
    {
        foreach (var name in pair.PauseWhileProcessesRunning)
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }

    private async Task<Dictionary<string, DriveFile>> EntriesAsync(string? parent, CancellationToken token, string? onlyName = null)
    {
        var entries = parent == null ? [] : await drive.ListAsync(parent, token);
        var result = new Dictionary<string, DriveFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in entries)
        {
            if (onlyName != null && !file.Name.Equals(onlyName, StringComparison.OrdinalIgnoreCase)) continue;
            PathSafety.Name(file.Name);
            if (!result.TryAdd(file.Name, file)) throw new IOException($"Ambiguous duplicate Google Drive name: {file.Name}");
        }
        return result;
    }

    private async Task<string?> FolderAsync(string? parent, string name, bool dryRun, CancellationToken token)
    {
        var entries = await EntriesAsync(parent, token, name);
        if (entries.TryGetValue(name, out var file))
        {
            if (!file.IsFolder) throw new IOException($"Expected a folder: {name}");
            return file.Id;
        }
        logger.LogInformation("Create Google folder: {Name}", name);
        return dryRun ? null : (await drive.CreateFolderAsync(parent!, name, token)).Id;
    }

    private async Task DirectoryAsync(SyncPair pair, string path, string? parent, bool dryRun, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (IsPaused(pair)) throw new IOException("Game started; synchronization paused.");
        PathSafety.NoLinks(path);
        if (File.Exists(path)) throw new IOException($"Expected a local folder: {path}");
        var remote = await EntriesAsync(parent, token);
        var local = Directory.Exists(path) ? Directory.GetFileSystemEntries(path).Where(p => !Path.GetFileName(p).StartsWith(".quirrel-", StringComparison.OrdinalIgnoreCase)).ToArray() : [];
        foreach (var entry in local) { PathSafety.Name(Path.GetFileName(entry)); PathSafety.NoLinks(entry); }
        if (!Directory.Exists(path))
        {
            logger.LogInformation("Create local folder: {Path}", path);
            if (!dryRun) Directory.CreateDirectory(path);
        }
        foreach (var name in local.Select(Path.GetFileName).Cast<string>().Union(remote.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var target = Path.Combine(path, name);
            remote.TryGetValue(name, out var item);
            var isDirectory = Directory.Exists(target);
            if (item != null && (File.Exists(target) || isDirectory) && item.IsFolder != isDirectory)
                throw new IOException($"File/folder collision: {target}");
            if (isDirectory || item?.IsFolder == true)
            {
                var folder = item?.Id ?? await FolderAsync(parent, name, dryRun, token);
                await DirectoryAsync(pair, target, folder, dryRun, token);
            }
            else await FileAsync(pair, target, parent, name, item, dryRun, token);
        }
    }

    private static async Task<string> HashAsync(Stream stream, CancellationToken token)
    {
        stream.Position = 0;
        var hash = Convert.ToHexString(await MD5.HashDataAsync(stream, token));
        stream.Position = 0;
        return hash;
    }
    private static bool Equal(string? a, string? b) => a != null && b != null && a.Equals(b, StringComparison.OrdinalIgnoreCase);
    private static bool Same(DriveFile a, DriveFile b) => a.ModifiedTime == b.ModifiedTime && a.Md5 == b.Md5 && a.Size == b.Size;
    private string BackupPath(string name, string originalPath, string side)
    {
        var folder = Path.Combine(config.DataDirectory, "backups", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + "-" + name);
        File.WriteAllText(path + ".json", System.Text.Json.JsonSerializer.Serialize(new { OriginalPath = originalPath, Side = side, CreatedUtc = DateTimeOffset.UtcNow }));
        return path;
    }
    private async Task VerifyRemoteAsync(DriveFile item, CancellationToken token)
    {
        if (!Same(item, await drive.GetAsync(item.Id, token))) throw new IOException("Remote file changed during transfer; retry later.");
    }
    private async Task DownloadCheckedAsync(DriveFile item, Stream target, CancellationToken token)
    {
        await drive.DownloadAsync(item.Id, target, token);
        if (item.Size != null && target.Length != item.Size) throw new IOException("Download size mismatch.");
        if (item.Md5 != null && !Equal(await HashAsync(target, token), item.Md5)) throw new IOException("Download checksum mismatch.");
        await VerifyRemoteAsync(item, token);
    }

    private async Task FileAsync(SyncPair pair, string path, string? parent, string name, DriveFile? remote, bool dryRun, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        PathSafety.NoLinks(path);
        if (Directory.Exists(path) || remote?.IsFolder == true) throw new IOException($"Expected a file: {path}");
        if (remote?.IsSupported == false) throw new IOException($"Unsupported Google document or shortcut: {name}");
        if (IsPaused(pair)) throw new IOException("Game started; synchronization paused.");
        var exists = File.Exists(path);
        if (!exists && remote == null) { logger.LogWarning("Neither file exists; no content to synchronize: {Path}", path); return; }
        var modified = exists ? new DateTimeOffset(File.GetLastWriteTimeUtc(path)) : DateTimeOffset.MinValue;
        if ((exists && DateTimeOffset.UtcNow - modified < TimeSpan.FromSeconds(config.SettleSeconds)) ||
            (remote != null && DateTimeOffset.UtcNow - remote.ModifiedTime < TimeSpan.FromSeconds(config.SettleSeconds)))
        { logger.LogInformation("Waiting for stable file: {Path}", path); return; }
        // Keep the read lock until atomic replacement; allow the replacement to rename
        // the old file while still denying writers access to its contents.
        using var source = exists ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete) : null;
        if (exists && File.GetLastWriteTimeUtc(path) != modified.UtcDateTime)
            throw new IOException("Local file changed before acquiring the read lock; retry later.");
        var hash = source == null ? null : await HashAsync(source, token);
        if (remote != null && Equal(hash, remote.Md5)) return;
        var upload = exists && (remote == null || modified >= remote.ModifiedTime);
        logger.LogInformation("{Action}: {Path}", upload ? "Upload" : "Download", path);
        if (dryRun) return;
        if (upload)
        {
            if (remote != null)
            {
                // Preserve the losing version even on first synchronization or equal timestamps.
                await using var backup = new FileStream(BackupPath(name, path, "Google Drive: " + remote.Id), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                await DownloadCheckedAsync(remote, backup, token);
                backup.Flush(true);
            }
            if (IsPaused(pair)) throw new IOException("Game started; retry later.");
            if (remote != null) await VerifyRemoteAsync(remote, token);
            await drive.UploadAsync(parent!, name, remote?.Id, source!, modified, token);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".quirrel-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    await DownloadCheckedAsync(remote!, target, token);
                    target.Flush(true);
                }
                if (IsPaused(pair)) throw new IOException("Game started; retry later.");
                File.SetLastWriteTimeUtc(temporary, remote!.ModifiedTime.UtcDateTime);
                if (exists)
                {
                    // Preserve exactly the version being replaced, including a last-moment
                    // rename by another process. The sibling is on the same volume.
                    var recovery = Path.Combine(Path.GetDirectoryName(path)!, ".quirrel-" + Guid.NewGuid().ToString("N") + ".recovery");
                    var backupPath = BackupPath(name, path, "Local");
                    using (var check = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    {
                        if (!Equal(hash, await HashAsync(check, token))) throw new IOException("Local file changed; retry later.");
                        PathSafety.NoLinks(path);
                        File.Replace(temporary, path, recovery);
                    }
                    source!.Dispose();
                    try { File.Move(recovery, backupPath); }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Replacement completed; original is preserved in recovery file: {Recovery}", recovery);
                        throw;
                    }
                }
                else File.Move(temporary, path, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
