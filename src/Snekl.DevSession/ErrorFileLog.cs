using System.Text;
using Microsoft.Extensions.Logging;

namespace Snekl.DevSession;

public static class ErrorFileLog
{
    private static readonly Mutex Gate = new(false, "Snekl.ErrorsLog");

    public static void Note(string path, string service, int pid)
    {
        Append(path, $"# {Stamp()} {service} started pid={pid}{Environment.NewLine}{Environment.NewLine}");
    }

    public static void Write(string path, string service, string category, Exception exception)
    {
        var builder = new StringBuilder();
        builder.Append(Stamp());
        builder.Append(" [").Append(service).Append("] ");
        builder.AppendLine(category);
        builder.AppendLine(exception.ToString());
        builder.AppendLine();
        Append(path, builder.ToString());
    }

    public static void Write(string path, string service, string category, string message, Exception? exception)
    {
        var builder = new StringBuilder();
        builder.Append(Stamp());
        builder.Append(" [").Append(service).Append("] ");
        builder.AppendLine(category);
        if (!string.IsNullOrWhiteSpace(message))
            builder.AppendLine(message);
        if (exception is not null && (message is null || !message.Contains(exception.GetType().FullName ?? exception.GetType().Name, StringComparison.Ordinal)))
            builder.AppendLine(exception.ToString());
        builder.AppendLine();
        Append(path, builder.ToString());
    }

    private static string Stamp() => DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

    private static void Append(string path, string text)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var acquired = false;
            try
            {
                acquired = Gate.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
                return;

            try
            {
                File.AppendAllText(path, text);
            }
            finally
            {
                Gate.ReleaseMutex();
            }
        }
        catch
        {
            // Error logging must not fail the request.
        }
    }
}

public sealed class ErrorFileLoggerProvider(string path, string service) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ErrorFileLogger(path, service, categoryName);

    public void Dispose()
    {
    }

    private sealed class ErrorFileLogger(string path, string service, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || category.StartsWith("Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddleware", StringComparison.Ordinal))
                return;

            ErrorFileLog.Write(path, service, category, formatter(state, exception), exception);
        }
    }
}
