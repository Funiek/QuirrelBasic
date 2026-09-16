using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.Upload;
using Google.Apis.Download;
using System.Security.Cryptography;
using QuirrelBasic.IServices;
using QuirrelBasic.Models;
using GFile = Google.Apis.Drive.v3.Data.File;

namespace QuirrelBasic.Services;
public sealed class GoogleDriveService(DriveService drive) : IDriveService, IDisposable
{
    private const string Fields = "id,name,mimeType,modifiedTime,md5Checksum,size,trashed";
    public const string FolderMime = "application/vnd.google-apps.folder";
    public static async Task<GoogleDriveService> ConnectAsync(DrivesConfig config, bool authorize, CancellationToken token)
    {
        using var stream = File.OpenRead(config.GoogleClientSecretPath);
        var secrets = GoogleClientSecrets.FromStream(stream).Secrets;
        var store = new FileDataStore(Path.Combine(config.DataDirectory, "tokens"), true);
        UserCredential credential;
        if (authorize)
            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(secrets, [DriveService.Scope.Drive], "quirrel", token, store);
        else
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            { ClientSecrets = secrets, Scopes = [DriveService.Scope.Drive], DataStore = store });
            var saved = await flow.LoadTokenAsync("quirrel", token);
            if (saved?.RefreshToken == null) throw new InvalidOperationException("Run authorize interactively before starting the service.");
            credential = new UserCredential(flow, "quirrel", saved);
        }
        return new GoogleDriveService(new DriveService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "QuirrelBasic" }));
    }
    private static DriveFile Map(GFile file)
    {
        if (file.Trashed == true) throw new IOException("Google Drive item is in trash.");
        var supported = !file.MimeType.StartsWith("application/vnd.google-apps.") || file.MimeType == FolderMime;
        return new(file.Id, file.Name, file.MimeType == FolderMime, file.ModifiedTimeDateTimeOffset ?? DateTimeOffset.MinValue, file.Md5Checksum, file.Size, supported);
    }
    public async Task<DriveFile> GetAsync(string id, CancellationToken token)
    {
        var request = drive.Files.Get(id); request.Fields = Fields;
        return Map(await request.ExecuteAsync(token));
    }
    public async Task<IReadOnlyList<DriveFile>> ListAsync(string parentId, CancellationToken token)
    {
        var result = new List<DriveFile>(); string? page = null;
        do
        {
            var request = drive.Files.List();
            request.Q = $"'{parentId.Replace("\\", "\\\\").Replace("'", "\\'")}' in parents and trashed = false";
            request.Fields = $"nextPageToken,files({Fields})"; request.PageSize = 1000; request.PageToken = page;
            var response = await request.ExecuteAsync(token);
            result.AddRange(response.Files.Select(Map)); page = response.NextPageToken;
        } while (page != null);
        return result;
    }
    public async Task<DriveFile> CreateFolderAsync(string parentId, string name, CancellationToken token)
    {
        var request = drive.Files.Create(new GFile { Name = name, MimeType = FolderMime, Parents = [parentId] });
        request.Fields = Fields;
        return Map(await request.ExecuteAsync(token));
    }
    public async Task DownloadAsync(string id, Stream target, CancellationToken token)
    {
        var result = await drive.Files.Get(id).DownloadAsync(target, token);
        if (result.Status != DownloadStatus.Completed) throw result.Exception ?? new IOException("Download failed.");
    }
    public async Task UploadAsync(string parentId, string name, string? id, Stream source, DateTimeOffset modified, CancellationToken token)
    {
        source.Position = 0;
        var expectedHash = Convert.ToHexString(await MD5.HashDataAsync(source, token));
        source.Position = 0;
        var metadata = new GFile { ModifiedTimeDateTimeOffset = modified };
        IUploadProgress result;
        GFile? uploaded;
        if (id == null)
        {
            metadata.Name = name; metadata.Parents = [parentId];
            var request = drive.Files.Create(metadata, source, "application/octet-stream");
            request.Fields = Fields;
            result = await request.UploadAsync(token);
            uploaded = request.ResponseBody;
        }
        else
        {
            var request = drive.Files.Update(metadata, id, source, "application/octet-stream");
            request.Fields = Fields;
            result = await request.UploadAsync(token);
            uploaded = request.ResponseBody;
        }
        if (result.Status != UploadStatus.Completed) throw result.Exception ?? new IOException("Upload failed.");
        if (uploaded == null || uploaded.Size != source.Length ||
            !expectedHash.Equals(uploaded.Md5Checksum, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Google Drive upload checksum or size mismatch.");
    }
    public void Dispose() => drive.Dispose();
}
