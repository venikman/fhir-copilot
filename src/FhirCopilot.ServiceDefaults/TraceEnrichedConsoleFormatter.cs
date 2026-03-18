using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

public sealed class TraceEnrichedConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "trace-enriched";
    private readonly ConsoleFormatterOptions _options;

    public TraceEnrichedConsoleFormatter(IOptions<ConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        _options = options.Value;
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null) return;

        var timestamp = _options.UseUtcTimestamp
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.Now;

        var activity = Activity.Current;

        textWriter.Write('[');
        textWriter.Write(timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        textWriter.Write("] [");
        textWriter.Write(logEntry.LogLevel);
        textWriter.Write(']');

        if (activity is not null)
        {
            textWriter.Write(" [");
            textWriter.Write(activity.TraceId.ToString()[..12]);
            textWriter.Write(' ');
            textWriter.Write(activity.SpanId.ToString());
            textWriter.Write(']');
        }

        textWriter.Write(' ');
        textWriter.Write(logEntry.Category);
        textWriter.Write(": ");
        textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine(logEntry.Exception.ToString());
        }
    }
}
