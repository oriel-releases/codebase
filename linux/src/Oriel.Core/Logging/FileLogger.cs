using Microsoft.Extensions.Logging;

namespace Oriel.Core.Logging;

/// <summary>Tiny daily-rolling file logger. Never pass secrets to log calls.</summary>
public sealed class FileLoggerProvider(string directory, LogLevel minLevel = LogLevel.Information) : ILoggerProvider
{
    readonly LogLevel _minLevel = minLevel;
    readonly Lock _gate = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, $"oriel-{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
            catch (IOException) { /* logging must never crash the app */ }
        }
    }

    sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        readonly string _short = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner._minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel.ToString()[..4].ToUpperInvariant()}] [{Environment.ProcessId}] {_short}: {formatter(state, exception)}";
            if (exception is not null) line += $" | {exception.GetType().Name}: {exception.Message}";
            owner.Write(line);
        }
    }
}
