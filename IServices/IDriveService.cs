using Google.Apis.Drive.v3;
using QuirrelBasic.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuirrelBasic.IServices
{
    public interface IDriveService
    {
        Task<List<DriveFile>> ListFilesInFolderAsync(CancellationToken stoppingToken);
        Task GetFile(string fileName, CancellationToken stoppingToken);
        Task UploadFile(string fileName, CancellationToken stoppingToken);
        Task DeleteFile(string fileName, CancellationToken stoppingToken);
        Task InitializeDriveServiceAsync(CancellationToken stoppingToken);
    }
}
