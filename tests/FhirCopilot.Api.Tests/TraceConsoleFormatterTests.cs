using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using FhirCopilot.ServiceDefaults;
using MEOptions = Microsoft.Extensions.Options.Options;

namespace FhirCopilot.Api.Tests;

public class TraceConsoleFormatterTests
{
    [Fact]
    public void Format_includes_traceId_and_spanId_when_activity_active()
    {
        var source = new ActivitySource("test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("test-span")!;
        Assert.NotNull(activity);

        var formatter = new TraceEnrichedConsoleFormatter(
            MEOptions.Create(new ConsoleFormatterOptions()));

        using var writer = new StringWriter();
        var entry = new LogEntry<string>(
            LogLevel.Information, "TestCategory", new EventId(0), "Hello world", null,
            (state, _) => state);

        formatter.Write(entry, null, writer);
        var output = writer.ToString();

        Assert.Contains(activity.TraceId.ToString()[..12], output);
        Assert.Contains("Information", output);
        Assert.Contains("Hello world", output);
    }

    [Fact]
    public void Format_omits_trace_bracket_when_no_activity()
    {
        Activity.Current = null;

        var formatter = new TraceEnrichedConsoleFormatter(
            MEOptions.Create(new ConsoleFormatterOptions()));

        using var writer = new StringWriter();
        var entry = new LogEntry<string>(
            LogLevel.Warning, "TestCategory", new EventId(0), "No trace", null,
            (state, _) => state);

        formatter.Write(entry, null, writer);
        var output = writer.ToString();

        Assert.Contains("Warning", output);
        Assert.Contains("No trace", output);
        Assert.DoesNotContain("[0000", output);
    }
}
