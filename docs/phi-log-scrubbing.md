# PHI Log Scrubbing Middleware

## Status: future — not yet implemented

## Problem

Even with POST-based endpoints (which keep PHI out of URLs), PHI can still leak into
application logs through:
- Exception stack traces that include request body content
- Framework-level request/response logging (e.g. `ILogger` at `Debug`/`Trace` level)
- Middleware that logs deserialized objects (`CopilotRequest.Query` contains patient names)
- Third-party library logs (HTTP client logging of FHIR responses)

## What to scrub

| Pattern | Example | Regex |
|---------|---------|-------|
| Patient names | `Alice Carter` | Names are hard to regex — use allowlist/denylist from the FHIR backend |
| Patient IDs | `patient-0001` | `patient[-/]\w+` |
| MRNs | `MRN: 123456` | `MRN[:\s]*\d+` |
| Dates of birth | `1979-03-15` | ISO date patterns in clinical context |
| SSNs | `123-45-6789` | `\d{3}-\d{2}-\d{4}` |
| FHIR resource paths | `Patient/patient-0001` | `(Patient\|Encounter\|Condition)/[\w-]+` |

## Implementation approach

### Option A: ASP.NET Core middleware (recommended for this project)

```csharp
public sealed class PhiRedactionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PhiRedactionMiddleware> _logger;

    public PhiRedactionMiddleware(RequestDelegate next, ILogger<PhiRedactionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Wrap the response body to intercept logs written during request processing
        await _next(context);
    }
}
```

Key: don't try to scrub the actual response — scrub what goes into **log sinks**.

### Option B: Custom `ILoggerProvider` with redaction

Wrap the default logger provider with a decorator that runs regex replacement
on every log message before it reaches the sink:

```csharp
public sealed class RedactingLogger : ILogger
{
    private readonly ILogger _inner;
    private static readonly Regex[] Patterns = { /* SSN, MRN, patient ID patterns */ };

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var redacted = RedactPatterns(message);
        _inner.Log(logLevel, eventId, redacted, exception,
            (s, _) => s);
    }

    private static string RedactPatterns(string input)
    {
        foreach (var pattern in Patterns)
            input = pattern.Replace(input, "[REDACTED]");
        return input;
    }
}
```

### Option C: Serilog destructuring policy

If using Serilog, create a `IDestructuringPolicy` that strips PHI fields
from structured log events before they reach any sink.

## What NOT to scrub

- FHIR resource **types** (Patient, Encounter) — these are not PHI
- Agent names, thread IDs, tool names — operational metadata
- Timestamps, status codes, latency — standard observability
- Error codes and categories — needed for debugging

## Testing strategy

1. Write integration tests that log a request containing known PHI patterns
2. Capture log output
3. Assert no PHI patterns appear in captured output
4. Run as part of CI

## References

- HIPAA-Compliant Logging in .NET: https://medium.com/bytehide/hipaa-compliant-logging-in-net-healthcare-applications-d3fd4bda121f
- AWS FHIR Works secure logging: https://github.com/aws-solutions/fhir-works-on-aws/blob/main/solutions/documentation/SECURE_LOGGING.md
- FHIR Security spec: https://hl7.org/fhir/security.html
