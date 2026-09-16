using System.Text.Json;
using QuirrelBasic;
using QuirrelBasic.IServices;
using QuirrelBasic.Models;
using QuirrelBasic.Services;

if (args.Length == 0 || args[0] is "help" or "--help")
{
    Console.WriteLine("QuirrelBasic <authorize|validate|once|dry-run|run> [absolute-config-path]");
    return 0;
}
try
{
    var command = args[0];
    if (args.Length > 2 || command is not ("authorize" or "validate" or "once" or "dry-run" or "run"))
        throw new ArgumentException("Unknown command. Use --help.");
    var configPath = Path.GetFullPath(args.Length == 2 ? args[1] : Path.Combine(AppContext.BaseDirectory, "drives_config.json"));
    var config = JsonSerializer.Deserialize<DrivesConfig>(await File.ReadAllTextAsync(configPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new ArgumentException("Empty configuration.");
    config.Validate(Path.GetDirectoryName(configPath)!);
    if (command == "validate") { Console.WriteLine("Configuration OK."); return 0; }
    Directory.CreateDirectory(config.DataDirectory);
    using var instanceLock = new FileStream(Path.Combine(config.DataDirectory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    using var drive = await GoogleDriveService.ConnectAsync(config, command == "authorize", cancellation.Token);
    if (command == "authorize") { Console.WriteLine("Authorization saved. You can start the service."); return 0; }
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory, Args = [] });
    builder.Services.AddWindowsService(options => options.ServiceName = "QuirrelBasic");
    builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(config.DataDirectory, "logs")));
    builder.Services.AddSingleton(config);
    builder.Services.AddSingleton<IDriveService>(drive);
    builder.Services.AddSingleton<SyncEngine>();
    if (command == "run") builder.Services.AddHostedService<Worker>();
    using var host = builder.Build();
    if (command == "run") await host.RunAsync(cancellation.Token);
    else await host.Services.GetRequiredService<SyncEngine>().RunAsync(command == "dry-run", cancellation.Token);
    return 0;
}
catch (OperationCanceledException) { return 0; }
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
