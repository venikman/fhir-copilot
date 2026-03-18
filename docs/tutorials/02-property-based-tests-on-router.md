# Property-Based Tests on the Intent Router

A starter template is not where you expect to find property-based testing.
Most starter projects ship a handful of example-based tests and call it done.
This codebase does something different: it uses **FsCheck** to prove structural
guarantees about the intent router -- the component that decides which
specialist agent handles every single user query.

This tutorial explains why that choice was made, how the tests work, and how
you can apply the same technique to your own projects.

---

## 1. What Are Property-Based Tests?

Traditional example-based tests verify specific input/output pairs:

```csharp
[InlineData("How many diabetic patients do we have?", "analytics")]
```

You pick inputs, you assert the expected outputs. This works well for
documenting known behavior, but it only covers the cases you thought of.

Property-based tests flip the approach. Instead of specifying individual cases,
you describe **invariants** -- properties that must hold for *all* inputs -- and
let a generator produce hundreds of random inputs to verify them:

```csharp
[Property]
public bool Always_returns_a_known_agent_type(NonEmptyString query)
{
    var result = _router.RouteAsync(query.Get, CancellationToken.None).Result;
    return AgentTypes.All.Contains(result, StringComparer.OrdinalIgnoreCase);
}
```

FsCheck generates random `NonEmptyString` values -- Unicode, punctuation,
numbers, multi-word strings, single characters -- and runs this test 100 times
by default. If even one random input violates the property, the test fails and
FsCheck reports the minimal failing case through its shrinking algorithm.

The key insight: you are not testing *which* agent gets selected. You are
testing that the router **always behaves correctly at a structural level**,
regardless of input.

---

## 2. The Five Properties Tested

The file `tests/FhirCopilot.Api.Tests/RouterPropertyTests.cs` defines five
properties. Each one guards a different structural guarantee of the router.

### Property 1: Always Returns a Known Agent Type

```csharp
[Property]
public bool Always_returns_a_known_agent_type(NonEmptyString query)
{
    var result = _router.RouteAsync(query.Get, CancellationToken.None).Result;
    return AgentTypes.All.Contains(result, StringComparer.OrdinalIgnoreCase);
}
```

**Why it matters:** The router sits at the front of every request. Its output
becomes a dictionary key to look up the agent profile. If it returns a string
that is not in `AgentTypes.All` -- say, a misspelled agent name, or an empty
string from a missed edge case -- the downstream lookup throws a
`KeyNotFoundException` and the user gets a 500 error. This property proves that
can never happen: every possible input maps to one of `lookup`, `search`,
`analytics`, `clinical`, `cohort`, or `export`.

### Property 2: Deterministic for Same Input

```csharp
[Property]
public bool Deterministic_for_same_input(NonEmptyString query)
{
    var first = _router.RouteAsync(query.Get, CancellationToken.None).Result;
    var second = _router.RouteAsync(query.Get, CancellationToken.None).Result;
    return first == second;
}
```

**Why it matters:** Imagine a user sends the same query twice and gets routed
to different agents each time. That would be confusing at best, and at worst it
would produce contradictory answers within the same conversation thread. This
property guarantees the router has no hidden state, no randomness, and no
race-condition sensitivity. Same input, same output, always.

### Property 3: Case Insensitive

```csharp
[Property]
public bool Case_insensitive(NonEmptyString query)
{
    var lower = _router.RouteAsync(query.Get.ToLowerInvariant(), CancellationToken.None).Result;
    var upper = _router.RouteAsync(query.Get.ToUpperInvariant(), CancellationToken.None).Result;
    return lower == upper;
}
```

**Why it matters:** Users type "How Many patients" and "how many patients" and
"HOW MANY PATIENTS." If casing changed the routing decision, some queries would
silently go to the wrong agent. The router normalizes via `Trim().ToLowerInvariant()`
on line 24 of `KeywordIntentRouter.cs` -- this property proves that
normalization is applied correctly across all code paths, not just the ones
you tested manually.

### Property 4: Never Returns Null or Empty

```csharp
[Property]
public bool Never_returns_null_or_empty(NonEmptyString query)
{
    var result = _router.RouteAsync(query.Get, CancellationToken.None).Result;
    return !string.IsNullOrWhiteSpace(result);
}
```

**Why it matters:** This is the "no silent failures" property. A null or empty
return from the router would propagate through the system as a subtle bug --
possibly selecting a default agent by accident, possibly throwing a
`NullReferenceException` three layers deeper where the stack trace gives you no
clue what went wrong. This property forces the router to always make an
explicit decision.

### Property 5: Whitespace Padding Does Not Change Result

```csharp
[Property]
public bool Whitespace_padding_does_not_change_result(NonEmptyString query)
{
    var clean = _router.RouteAsync(query.Get, CancellationToken.None).Result;
    var padded = _router.RouteAsync($"  {query.Get}  ", CancellationToken.None).Result;
    return clean == padded;
}
```

**Why it matters:** User input is messy. Browsers may add trailing spaces.
Copy-paste from documents often includes leading whitespace. Mobile keyboards
sometimes insert extra spaces. If the router treated `"  how many  "` differently
from `"how many"`, you would have an intermittent bug that only surfaces for
some users on some devices. This property proves the `Trim()` in the router
works comprehensively.

---

## 3. The InMemoryProfileStore Trick

Look at how the test class sets up the router:

```csharp
public class RouterPropertyTests
{
    private readonly KeywordIntentRouter _router;

    public RouterPropertyTests()
    {
        _router = new KeywordIntentRouter(
            new InMemoryProfileStore(),
            NullLogger<KeywordIntentRouter>.Instance
        );
    }
```

The `KeywordIntentRouter` constructor requires an `IAgentProfileStore` -- in
production, that is `FileAgentProfileStore`, which reads JSON files from disk
via the DI container. But the property tests do not need the full DI container,
the filesystem, or the configuration system. They need exactly one thing: the
keyword hints map.

The test defines a minimal implementation right inside the test class:

```csharp
private sealed class InMemoryProfileStore : IAgentProfileStore
{
    private readonly RouterProfile _router = new()
    {
        KeywordHints = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["lookup"]    = ["show me", "read", "what is", "who manages",
                            "what insurance", "coverage for", "patient/",
                            "encounter/", "condition/"],
            ["search"]    = ["find patients", "search", "encounters for",
                            "patients by", "list encounters", "list patients"],
            ["analytics"] = ["how many", "count", "compare", "breakdown",
                            "trend", "top", "volume", "ratio", "percentage"],
            ["clinical"]  = ["clinical summary", "summarize", "tell me about",
                            "what happened", "full summary", "plain english"],
            ["cohort"]    = ["without", "who needs", "care gap", "gap",
                            "at risk", "patients with", "patients without",
                            "flag for review"],
            ["export"]    = ["export", "bulk", "download all", "snapshot",
                            "extract"]
        }
    };

    public AgentProfile GetAgent(string name) => throw new NotImplementedException();
    public RouterProfile GetRouter() => _router;
    public IReadOnlyDictionary<string, AgentProfile> GetAllAgents()
        => throw new NotImplementedException();
}
```

Notice two things:

1. **Only `GetRouter()` is implemented.** The other two methods throw
   `NotImplementedException`. This is deliberate: the router only calls
   `GetRouter()`. If someone accidentally adds a call to `GetAgent()` in the
   router, the test will fail loudly rather than silently returning bad data.

2. **The keyword hints mirror `router.json` but live in code.** This means the
   property tests are self-contained -- they do not depend on file paths,
   working directories, or build output locations. They run identically on any
   machine, in any CI environment.

This pattern -- implementing only the interface methods your subject-under-test
actually uses, and letting the rest throw -- is a lightweight alternative to
mocking frameworks. It makes dependencies explicit without adding a library
dependency.

---

## 4. Property Tests vs Example Tests: How They Complement Each Other

This codebase has **both** kinds of tests for the router. Compare:

### Example-based: `RoutingTests.cs`

```csharp
[Theory]
[InlineData("What insurance does patient-0001 have?", "lookup")]
[InlineData("Find patients named Carter", "search")]
[InlineData("How many diabetic patients do we have?", "analytics")]
[InlineData("Clinical summary for patient-0001", "clinical")]
[InlineData("Which diabetic patients are without metformin?", "cohort")]
[InlineData("Export all data for the Northwind group", "export")]
// ... 14 cases total
public async Task Query_routes_to_expected_agent(string query, string expectedAgent)
```

These tests verify **specific routing decisions**: "this exact query goes to
this exact agent." They are the specification. They document the intended
behavior for realistic user inputs. When someone changes the keyword hints or
the scoring logic, these tests catch regressions in routing accuracy.

But they only cover 14 inputs. The router will see thousands of different
queries in production.

### Property-based: `RouterPropertyTests.cs`

The five property tests do not care *which* agent gets selected. They prove
**structural guarantees** that must hold for every possible input:

| Concern | Example tests cover it? | Property tests cover it? |
|---|---|---|
| "diabetes query goes to analytics" | Yes | No |
| "no input can crash the router" | Only for 14 inputs | For all inputs |
| "router is deterministic" | Implicitly (tests are deterministic) | Explicitly proven |
| "casing does not matter" | Not tested | Proven for all inputs |
| "whitespace does not matter" | Not tested | Proven for all inputs |

The two test suites guard different failure modes:

- **Example tests** catch: "we changed the keyword hints and now diabetes
  queries go to the wrong agent."
- **Property tests** catch: "we refactored the scoring logic and now some
  Unicode input causes a `KeyNotFoundException`."

Together, they give you both **correctness of specific decisions** and
**robustness across all inputs**.

---

## 5. How to Apply This to Your Own Code

Property-based tests are most valuable on components that have **structural
invariants** -- rules that must hold regardless of input. Look for these
patterns in your codebase:

### Routers / Dispatchers

Any component that maps an input to one of N known outputs:

- Must always return a valid output (never null, never an unknown value)
- Must be deterministic
- Should be insensitive to trivial input variations (casing, whitespace)

### Parsers

Components that transform unstructured input to structured output:

```csharp
[Property]
public bool Parse_never_throws(NonEmptyString input)
{
    // Should return a result or an error, never an unhandled exception
    var result = Parser.TryParse(input.Get, out var parsed);
    return result == true || result == false; // no exception path
}

[Property]
public bool Parse_then_format_roundtrips(WellFormedInput input)
{
    var parsed = Parser.Parse(input.Value);
    var formatted = parsed.ToString();
    var reparsed = Parser.Parse(formatted);
    return parsed.Equals(reparsed);
}
```

### Validators

Components that accept or reject input:

```csharp
[Property]
public bool Valid_input_is_always_accepted(ValidInput input)
{
    return Validator.IsValid(input.Value);
}

[Property]
public bool Validation_is_deterministic(NonEmptyString input)
{
    return Validator.IsValid(input.Get) == Validator.IsValid(input.Get);
}
```

### Normalizers / Sanitizers

Components that clean up input:

```csharp
[Property]
public bool Normalizing_twice_is_same_as_once(NonEmptyString input)
{
    var once = Normalizer.Normalize(input.Get);
    var twice = Normalizer.Normalize(once);
    return once == twice; // idempotency
}
```

The common thread: these are all **pure-ish functions** at system boundaries.
They take input, produce output, and should not have surprising edge cases.
Property-based tests are how you prove they do not.

---

## 6. FsCheck + xUnit Integration

The integration between FsCheck and xUnit is minimal, which is part of its
appeal.

### The NuGet packages

The project references `FsCheck.Xunit`, which provides the `[Property]`
attribute and wires FsCheck's test runner into xUnit's test discovery.

### The `[Property]` attribute

Replace `[Fact]` or `[Theory]` with `[Property]`:

```csharp
[Property]
public bool Always_returns_a_known_agent_type(NonEmptyString query)
```

FsCheck sees the method parameter `NonEmptyString query` and automatically
selects a generator for it. It then runs the method 100 times (by default)
with different random values. If the method returns `false` for any input,
the test fails.

### The `NonEmptyString` generator

FsCheck includes built-in generators for common constrained types.
`NonEmptyString` guarantees the generated string is never null or empty -- which
is appropriate here because routing an empty string is not a meaningful scenario
(and would be caught by input validation before reaching the router).

Other useful built-in generators include:

- `PositiveInt` -- integers > 0
- `NonNegativeInt` -- integers >= 0
- `NormalFloat` -- floats that are not NaN or Infinity

### Return type: `bool`

The simplest FsCheck pattern is to return a `bool`. Return `true` if the
property holds, `false` if it does not. FsCheck treats a `false` return as a
test failure and reports the input that caused it.

For more complex assertions, you can return `Property` instead:

```csharp
[Property]
public Property Example(NonEmptyString input)
{
    var result = DoSomething(input.Get);
    return (result != null).Label("result should not be null")
       .And((result.Length > 0).Label("result should not be empty"));
}
```

### Shrinking

When FsCheck finds a failing input, it does not just report it. It **shrinks**
the input -- tries progressively simpler versions to find the *minimal*
failing case. If your router fails on a 47-character Unicode string, FsCheck
will narrow it down to the shortest string that still triggers the failure.
This makes debugging far easier than staring at a random 47-character string.

### Configuration

You can adjust the number of test cases per property:

```csharp
[Property(MaxTest = 500)]
public bool More_thorough_check(NonEmptyString query) { ... }
```

Or set a global default in your test project by creating an `Arbitrary`
implementation with custom generators.

---

## Summary

The intent router is a small component -- about 40 lines of logic in
`KeywordIntentRouter.cs`. But it is on the critical path of every request.
Every query the user sends passes through it. A bug in the router does not
just break one feature; it can misroute any query to the wrong agent.

The five property tests in `RouterPropertyTests.cs` prove, for hundreds of
random inputs, that the router:

1. Always picks a valid agent
2. Is deterministic
3. Ignores casing
4. Never returns nothing
5. Ignores whitespace padding

These are not the kind of guarantees you get from 14 example-based tests.
They are the kind of guarantees you need from a component that every request
flows through.

The surprising part is not that property-based tests exist. It is that they
are in a *starter template*. The message is clear: these five properties are
not optional hardening to add later. They are part of the contract from day
one.
