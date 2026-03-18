# Step 7: Functional Refactors

## What
Code quality improvements: primary constructors, immutable records, extracted helpers, input validation.

## Changes Made

### FhirToolbox — primary constructor + Calculator guard
**Before:**
```csharp
public sealed class FhirToolbox
{
    private readonly IFhirBackend _backend;
    public FhirToolbox(IFhirBackend backend) { _backend = backend; }
}
```
**After:**
```csharp
public sealed class FhirToolbox(IFhirBackend backend)
{
    private readonly IFhirBackend _backend = backend;
}
```

**Calculator guard** — `DataTable.Compute` accepts arbitrary expressions. Added regex validation to only allow digits, operators, parentheses, and whitespace:
```csharp
private static readonly Regex SafeExpressionPattern = new(
    @"^[\d\s\+\-\*\/\.\(\)\%]+$", RegexOptions.Compiled);
```
Returns a descriptive error instead of throwing on invalid input.

### GeminiAgentFrameworkRunner — immutable SessionEntry
**Before:** mutable `set` on LastUsed (breaks record value semantics)
```csharp
private sealed record SessionEntry(AgentSession Session, DateTime LastUsed)
{
    public DateTime LastUsed { get; set; } = LastUsed;
}
// Usage: existing.LastUsed = DateTime.UtcNow;
```
**After:** immutable with `with` expression
```csharp
private sealed record SessionEntry(AgentSession Session, DateTime LastUsed);
// Usage: _sessions[key] = existing with { LastUsed = DateTime.UtcNow };
```

### SseWriter — extracted SSE streaming logic
Moved 25 lines of SSE serialization, error handling, and client disconnection logic from Program.cs into `SseWriter.WriteAsync()`. Program.cs stream endpoint reduced to one line:
```csharp
await SseWriter.WriteAsync(httpContext, service.StreamAsync(request, cancellationToken), cancellationToken);
```

## Why these matter
- **Primary constructors** — less boilerplate, intent is clearer
- **Immutable records** — prevents accidental mutation outside the lock; `with` makes intent explicit
- **SseWriter** — Program.cs stays focused on routing, SSE logic is testable independently
- **Calculator guard** — prevents injection via `DataTable.Compute` which can evaluate arbitrary .NET expressions
