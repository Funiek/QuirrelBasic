namespace QuirrelBasic.Services;
public sealed class FileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly string logDirectory = directory;
    private readonly object gate = new();
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }
    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            lock (owner.gate)
            {
                Directory.CreateDirectory(owner.logDirectory);
                File.AppendAllText(Path.Combine(owner.logDirectory, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log"),
                    $"{DateTimeOffset.UtcNow:O} [{level}] {category}: {formatter(state, exception)} {exception}{Environment.NewLine}");
            }
        }
    }
}
