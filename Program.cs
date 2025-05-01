using QuirrelBasic;
using QuirrelBasic.IServices;
using QuirrelBasic.Models;
using QuirrelBasic.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "drives_config.json"), optional: false, reloadOnChange: true);
DrivesConfig drivesConfig = new DrivesConfig(args[0], args[1]);

builder.Services.AddLogging();
builder.Services.AddTransient<IDriveService>(x => new GoogleDriveService(
   x.GetRequiredService<ILogger<GoogleDriveService>>(),
   drivesConfig.GoogleFolderId,
   drivesConfig.GoogleClientSecretPath
));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
