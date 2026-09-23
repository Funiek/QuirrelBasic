using QuirrelBasic.Models;
namespace QuirrelBasic.IServices;
public interface IDriveService
{
    Task<IReadOnlyList<DriveFile>> ListAsync(string parentId, CancellationToken token);
    Task<DriveFile> GetAsync(string id, CancellationToken token);
    Task<DriveFile> CreateFolderAsync(string parentId, string name, CancellationToken token);
    Task DownloadAsync(string id, Stream target, CancellationToken token);
    Task UploadAsync(string parentId, string name, string? id, Stream source, DateTimeOffset modified, CancellationToken token);
}
