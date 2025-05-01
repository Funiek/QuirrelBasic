using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using QuirrelBasic.IServices;
using QuirrelBasic.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuirrelBasic.Services
{
    public class GoogleDriveService : IDriveService
    {
        private readonly ILogger _logger;
        private DriveService _driveService = null!;
        private readonly string _googleDriveFolderId;
        private readonly string _credentialsFilePath;

        public GoogleDriveService(ILogger logger, string googleDriveFolderId, string credentialsFilePath)
        {
            _logger = logger;
            _googleDriveFolderId = googleDriveFolderId;
            _credentialsFilePath = credentialsFilePath;
        }

        public async Task InitializeDriveServiceAsync(CancellationToken stoppingToken)
        {
            if (_driveService is not null)
            {
                _logger.LogInformation("Google Drive service is already initialized.");
                return;
            }

            try
            {
                using (var stream = new FileStream(_credentialsFilePath, FileMode.Open, FileAccess.Read))
                {
                    var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        new[] { "https://www.googleapis.com/auth/drive" },
                        "quirrel-client",
                        stoppingToken);

                    _driveService = new DriveService(new BaseClientService.Initializer
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = "Quirrel"
                    });

                    _logger.LogInformation("Google Drive service initialized successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize Google Drive service: {ex.Message}");
                throw;
            }
        }

        private void EnsureInitialized()
        {
            if (_driveService is null)
            {
                throw new InvalidOperationException("GoogleDriveService is not initialized. Call InitializeGoogleDriveServiceAsync first.");
            }
        }

        public async Task<List<DriveFile>> ListFilesInFolderAsync(CancellationToken stoppingToken)
        {
            var files = new List<DriveFile>();

            try
            {
                EnsureInitialized();
                var request = _driveService.Files.List();
                request.Q = $"'{_googleDriveFolderId}' in parents and trashed = false";
                request.Fields = "files(id, name, modifiedTime)";

                var result = await request.ExecuteAsync(stoppingToken);

                if (result.Files != null && result.Files.Count > 0)
                {
                    foreach (var file in result.Files)
                    {
                        files.Add(new DriveFile
                        {
                            Id = file.Id,
                            Name = file.Name,
                            ModifiedTime = file.ModifiedTimeDateTimeOffset
                        });
                    }
                }
                else
                {
                    _logger.LogInformation("No files found in the specified folder.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while listing files: {ex.Message}");
            }

            return files;
        }

        public Task GetFile(string fileName, CancellationToken stoppingToken)
        {
            throw new NotImplementedException();
        }

        public Task UploadFile(string fileName, CancellationToken stoppingToken)
        {
            throw new NotImplementedException();
        }

        public Task DeleteFile(string fileName, CancellationToken stoppingToken)
        {
            throw new NotImplementedException();
        }

        public String getToken()
        {
            return _credentialsFilePath;
        }
    }
}
