using Microsoft.Extensions.Logging;

namespace Swankers.League.Tests.Support;

/// <summary>Captures formatted log output, including exception text, for secrecy assertions.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Entries { get; } = [];

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add(formatter(state, exception) + (exception is null ? "" : " | " + exception));
}
