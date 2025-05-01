using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using QuirrelBasic.IServices;
using QuirrelBasic.Models;
using QuirrelBasic.Services;

namespace QuirrelBasic
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IDriveService _driveService;

        public Worker(ILogger<Worker> logger, IDriveService driveService)
        {
            _logger = logger;
            _driveService = driveService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _driveService.InitializeDriveServiceAsync(stoppingToken);
            var folderFiles = await _driveService.ListFilesInFolderAsync(stoppingToken);

            foreach (var file in folderFiles)
            {
                _logger.LogInformation(file.ToString());
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
    }
}
