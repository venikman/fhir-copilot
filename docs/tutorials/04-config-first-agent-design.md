# Config-First Agent Design with Startup Validation

This tutorial explains a design choice in the FHIR Copilot Agent Framework that
prevents an entire class of bugs: defining agents in external JSON configuration
files and validating those definitions at application startup, before a single
request is served.

The pattern is simple. The payoff is large. Once you see how it works, you will
wonder why most agent frameworks still hard-code prompts in service classes.

---

## 1. The Problem with Code-Embedded Prompts

In most AI agent systems, the pieces that define an agent's behavior are scattered
across the codebase:

- **Prompts** live inside C# string literals, Python f-strings, or TypeScript
  template literals, buried in service classes.
- **Tool permissions** are implicit -- whatever tools are wired into the DI
  container are available to every agent.
- **Behavioral rules** ("lead with the answer", "include citations") are
  sprinkled as ad-hoc prompt fragments across multiple files.

This creates several concrete problems:

1. **Changing a prompt means changing code.** You edit a `.cs` file, recompile,
   redeploy, and hope your one-line prompt tweak did not break the surrounding
   logic.

2. **Reviewing prompt changes requires reading code diffs.** A product manager
   or clinician who wants to verify that "the agent should summarize allergies"
   is correct behavior has to read a C# diff, not a plain-text diff.

3. **Tool access is all-or-nothing.** Without explicit per-agent tool lists,
   every agent gets every tool, which violates least privilege and makes it
   impossible to reason about what an agent can and cannot do.

4. **Typos in tool names surface at request time.** If you misspell
   `"search_patients"` as `"search_patient"`, you find out when a user triggers
   the misconfigured path -- possibly in production.

The config-first pattern solves all four problems.

---

## 2. The JSON Profile Structure

Every agent in this system is defined by a JSON file in
`src/FhirCopilot.Api/config/agents/`. Each file maps directly to the
`AgentProfile` model:

```csharp
// src/FhirCopilot.Api/Models/AgentConfigModels.cs

public sealed class AgentProfile
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public string PreferredApi { get; init; } = "ChatCompletion";
    public string[] AllowedTools { get; init; } = Array.Empty<string>();
    public string[] Instructions { get; init; } = Array.Empty<string>();
    public string[] DomainContext { get; init; } = Array.Empty<string>();
    public ResponseContract ResponseContract { get; init; } = new();
}

public sealed class ResponseContract
{
    public bool AnswerFirst { get; init; } = true;
    public bool IncludeCitations { get; init; } = true;
    public bool IncludeReasoningSummary { get; init; } = true;
    public string[] StructuredSections { get; init; } = Array.Empty<string>();
}
```

The richest example is the clinical agent. Here is its full profile:

```json
{
  "name": "clinical",
  "displayName": "Clinical",
  "purpose": "Multi-resource patient summaries and plain-English encounter narratives.",
  "preferredApi": "ChatCompletion",
  "allowedTools": [
    "search_groups",
    "read_resource",
    "list_resources",
    "bulk_export",
    "search_patients",
    "search_encounters",
    "search_conditions",
    "search_observations",
    "search_medications",
    "search_procedures",
    "search_allergies",
    "calculator"
  ],
  "instructions": [
    "Build a coherent patient story across demographics, conditions, medications, observations, encounters, and allergies.",
    "Translate coded clinical facts into plain English.",
    "Keep the structure stable across summaries."
  ],
  "domainContext": [
    "Clinical answers may cross multiple resource types.",
    "Narrative synthesis is the primary goal."
  ],
  "responseContract": {
    "answerFirst": true,
    "includeCitations": true,
    "includeReasoningSummary": true,
    "structuredSections": [
      "Demographics",
      "Conditions",
      "Medications",
      "Observations",
      "Encounters",
      "Allergies"
    ]
  }
}
```

Each field has a clear purpose:

| Field | Role |
|---|---|
| `name` | Routing key -- the string the router uses to dispatch to this agent |
| `displayName` | Model-facing name used in the system prompt ("You are the Clinical agent...") |
| `purpose` | One-sentence description injected into the prompt and useful for documentation |
| `preferredApi` | Signal for which OpenAI API surface to use (ChatCompletion vs. Responses) |
| `allowedTools` | Explicit subset of the 12 known tools this agent is permitted to call |
| `instructions` | Ordered behavioral rules, rendered as a numbered list in the system prompt |
| `domainContext` | Background facts rendered as bullet points |
| `responseContract` | Structural rules for the agent's output format |

Every piece of agent behavior lives in this file. No C# class needs to change
when you want the clinical agent to "Include allergy severity in summaries" --
you add a string to `instructions` and redeploy the config.

---

## 3. Least-Privilege Tool Access

The `allowedTools` array is where security-through-configuration happens. Not
every agent gets every tool. Compare three agents side by side:

**Lookup agent** -- 3 tools:
```json
"allowedTools": [
    "search_groups",
    "read_resource",
    "list_resources"
]
```

**Export agent** -- 3 tools:
```json
"allowedTools": [
    "search_groups",
    "read_resource",
    "bulk_export"
]
```

**Clinical agent** -- all 12 tools:
```json
"allowedTools": [
    "search_groups", "read_resource", "list_resources", "bulk_export",
    "search_patients", "search_encounters", "search_conditions",
    "search_observations", "search_medications", "search_procedures",
    "search_allergies", "calculator"
]
```

The enforcement happens in `ToolRegistry.BuildTools()`:

```csharp
public static IReadOnlyList<AIFunction> BuildTools(
    FhirToolbox toolbox,
    IEnumerable<string> allowedToolNames)
{
    var all = new Dictionary<string, AIFunction>(StringComparer.OrdinalIgnoreCase)
    {
        ["search_groups"]       = AIFunctionFactory.Create(toolbox.SearchGroups),
        ["read_resource"]       = AIFunctionFactory.Create(toolbox.ReadResource),
        ["list_resources"]      = AIFunctionFactory.Create(toolbox.ListResources),
        ["bulk_export"]         = AIFunctionFactory.Create(toolbox.BulkExport),
        ["search_patients"]     = AIFunctionFactory.Create(toolbox.SearchPatients),
        ["search_encounters"]   = AIFunctionFactory.Create(toolbox.SearchEncounters),
        ["search_conditions"]   = AIFunctionFactory.Create(toolbox.SearchConditions),
        ["search_observations"] = AIFunctionFactory.Create(toolbox.SearchObservations),
        ["search_medications"]  = AIFunctionFactory.Create(toolbox.SearchMedications),
        ["search_procedures"]   = AIFunctionFactory.Create(toolbox.SearchProcedures),
        ["search_allergies"]    = AIFunctionFactory.Create(toolbox.SearchAllergies),
        ["calculator"]          = AIFunctionFactory.Create(toolbox.Calculator)
    };

    var selected = new List<AIFunction>();

    foreach (var toolName in allowedToolNames.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (all.TryGetValue(toolName, out var function))
        {
            selected.Add(function);
        }
    }

    return selected;
}
```

The method builds the full dictionary of all 12 tools, then filters it to only
the names listed in `allowedToolNames`. Tools not in the list are never passed
to the model. An export agent literally cannot search conditions, even if the
LLM generates a tool call for it, because that tool was never included in the
function list sent to the model.

This is a meaningful security boundary. The LLM cannot call tools it was never
given. And the list of what each agent can call is a simple JSON array, auditable
by anyone who can read a config file.

---

## 4. The Router Config

Routing is also configuration, not code. Here is `router.json`:

```json
{
  "name": "router",
  "fallbackAgent": "clinical",
  "keywordHints": {
    "lookup": [
      "show me", "read", "what is", "who manages",
      "what insurance", "coverage for",
      "patient/", "encounter/", "condition/"
    ],
    "search": [
      "find patients", "search", "encounters for",
      "patients by", "list encounters", "list patients"
    ],
    "analytics": [
      "how many", "count", "compare", "breakdown",
      "trend", "top", "volume", "ratio", "percentage"
    ],
    "clinical": [
      "clinical summary", "summarize", "tell me about",
      "what happened", "full summary", "plain english"
    ],
    "cohort": [
      "without", "who needs", "care gap", "gap", "at risk",
      "patients with", "patients without", "flag for review"
    ],
    "export": [
      "export", "bulk", "download all", "snapshot", "extract"
    ]
  }
}
```

The routing model is defined by `RouterProfile`:

```csharp
public sealed class RouterProfile
{
    public string Name { get; init; } = "router";
    public string FallbackAgent { get; init; } = "clinical";
    public Dictionary<string, string[]> KeywordHints { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}
```

Two things to notice:

1. **The keyword hints are reviewable text.** If a clinician says "when I type
   'care gap' it should go to the cohort agent, not clinical," you can verify
   that directly by reading the JSON. No need to trace through a switch
   statement or a chain of if-else blocks.

2. **The fallback is explicit.** When no keyword matches, the system falls back
   to `"clinical"`. This default is visible and changeable without touching code.

---

## 5. Startup Validation with ToolRegistry.ValidateProfiles

Here is the method that catches configuration errors before any request is
served:

```csharp
private static readonly HashSet<string> KnownToolNames =
    new(StringComparer.OrdinalIgnoreCase)
{
    "search_groups", "read_resource", "list_resources", "bulk_export",
    "search_patients", "search_encounters", "search_conditions",
    "search_observations", "search_medications", "search_procedures",
    "search_allergies", "calculator"
};

public static void ValidateProfiles(IAgentProfileStore profileStore, ILogger logger)
{
    foreach (var (agentName, profile) in profileStore.GetAllAgents())
    {
        foreach (var toolName in profile.AllowedTools)
        {
            if (!KnownToolNames.Contains(toolName))
            {
                logger.LogWarning(
                    "Agent profile '{AgentName}' references unknown tool '{ToolName}'"
                    + " -- this tool will be silently ignored at runtime",
                    agentName, toolName);
            }
        }
    }
}
```

And here is how it is called in `Program.cs`, immediately after the application
is built and before any endpoint is mapped:

```csharp
var app = builder.Build();

// Validate agent tool configs at startup to catch typos early
ToolRegistry.ValidateProfiles(
    app.Services.GetRequiredService<IAgentProfileStore>(),
    app.Services.GetRequiredService<ILogger<Program>>());
```

The validation logic is straightforward:

1. Load every agent profile from disk.
2. For each profile, iterate through its `allowedTools` array.
3. Check every tool name against the `KnownToolNames` set (the 12 canonical
   tool names).
4. If a tool name is not recognized, log a warning with the agent name and the
   offending tool name.

This catches a specific class of bug: typos in tool names. Imagine you add a
new agent profile and accidentally write `"search_patient"` (missing the
trailing `s`). Without startup validation, this tool name would silently be
ignored by `BuildTools()` -- the agent would work but would mysteriously lack
the ability to search patients. You would discover this in production, or worse,
a clinician would discover it.

With startup validation, the application logs a clear warning at boot:

```
Agent profile 'my_new_agent' references unknown tool 'search_patient' -- this
tool will be silently ignored at runtime
```

You see this immediately in your deployment logs. The bug never reaches a user.

---

## 6. PromptComposer: Turning Config into System Prompts

The `PromptComposer` is a pure function. It takes an `AgentProfile` and returns
a fully-formed system prompt string. No side effects, no dependencies, no state:

```csharp
public static class PromptComposer
{
    public static string Compose(AgentProfile profile)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            $"You are the {profile.DisplayName} agent for a "
            + "provider-population copilot working over FHIR R4.");
        builder.AppendLine($"Purpose: {profile.Purpose}");
        builder.AppendLine();

        if (profile.DomainContext.Length > 0)
        {
            builder.AppendLine("Domain context:");
            foreach (var line in profile.DomainContext)
            {
                builder.AppendLine($"- {line}");
            }
            builder.AppendLine();
        }

        if (profile.Instructions.Length > 0)
        {
            builder.AppendLine("Operating instructions:");
            for (var index = 0; index < profile.Instructions.Length; index++)
            {
                builder.AppendLine($"{index + 1}. {profile.Instructions[index]}");
            }
            builder.AppendLine();
        }

        builder.AppendLine("Response contract:");
        builder.AppendLine(profile.ResponseContract.AnswerFirst
            ? "- Lead with the direct answer."
            : "- Do not force answer-first output.");
        builder.AppendLine(profile.ResponseContract.IncludeCitations
            ? "- Include resource citations when evidence is available."
            : "- Citations are optional.");
        builder.AppendLine(profile.ResponseContract.IncludeReasoningSummary
            ? "- Include a short reasoning summary."
            : "- Do not emit a reasoning summary unless needed.");

        if (profile.ResponseContract.StructuredSections.Length > 0)
        {
            builder.AppendLine("- Use these sections when relevant:");
            foreach (var section in profile.ResponseContract.StructuredSections)
            {
                builder.AppendLine($"  - {section}");
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            "Never dump raw JSON when a plain-English synthesis is possible.");

        return builder.ToString();
    }
}
```

For the clinical agent, this produces a system prompt like:

```
You are the Clinical agent for a provider-population copilot working over FHIR R4.
Purpose: Multi-resource patient summaries and plain-English encounter narratives.

Domain context:
- Clinical answers may cross multiple resource types.
- Narrative synthesis is the primary goal.

Operating instructions:
1. Build a coherent patient story across demographics, conditions, medications, observations, encounters, and allergies.
2. Translate coded clinical facts into plain English.
3. Keep the structure stable across summaries.

Response contract:
- Lead with the direct answer.
- Include resource citations when evidence is available.
- Include a short reasoning summary.
- Use these sections when relevant:
  - Demographics
  - Conditions
  - Medications
  - Observations
  - Encounters
  - Allergies

Never dump raw JSON when a plain-English synthesis is possible.
```

The key design property: every element of this prompt came from the JSON file.
The composer adds structure (headers, numbering, bullet prefixes) but never adds
content. If you want to change what the agent does, you change the config. If
you want to change how the config is rendered into a prompt, you change the
composer. These concerns are separated.

---

## 7. FileAgentProfileStore: Lazy Loading with Fail-Fast Behavior

The `FileAgentProfileStore` loads profiles from disk once and caches them for
the lifetime of the application:

```csharp
public sealed class FileAgentProfileStore : IAgentProfileStore
{
    private readonly Lazy<LoadedProfiles> _loaded;

    public FileAgentProfileStore(
        IHostEnvironment environment,
        IOptions<RuntimeOptions> runtimeOptions)
    {
        _loaded = new Lazy<LoadedProfiles>(() =>
        {
            var root = Path.Combine(
                environment.ContentRootPath,
                runtimeOptions.Value.AgentProfilesPath);

            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException(
                    $"Agent profiles path was not found: {root}");
            }

            var router = LoadFile<RouterProfile>(
                Path.Combine(root, "router.json"));

            var agents = Directory.GetFiles(
                    root, "*.json", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(
                    Path.GetFileName(path), "router.json",
                    StringComparison.OrdinalIgnoreCase))
                .Select(LoadAgentFile)
                .ToDictionary(
                    profile => profile.Name,
                    StringComparer.OrdinalIgnoreCase);

            return new LoadedProfiles(router, agents);
        });
    }
    // ...
}
```

Three fail-fast behaviors protect against silent misconfiguration:

1. **Missing directory** -- If the `config/agents/` directory does not exist,
   the store throws `DirectoryNotFoundException` immediately. The application
   will not start with a missing config directory.

2. **Deserialization failure** -- If a JSON file cannot be parsed, `LoadFile<T>`
   throws `InvalidDataException`. A malformed JSON file is caught the first time
   any profile is accessed.

3. **Missing name** -- If an agent profile does not declare a `name` field,
   `LoadAgentFile` throws `InvalidDataException`. Every profile must be
   addressable by name.

```csharp
private static AgentProfile LoadAgentFile(string path)
{
    var profile = LoadFile<AgentProfile>(path);
    if (string.IsNullOrWhiteSpace(profile.Name))
    {
        throw new InvalidDataException(
            $"Agent profile at '{path}' does not declare a name.");
    }
    return profile;
}
```

The `Lazy<T>` wrapper ensures this loading happens exactly once, on first
access. Since `ToolRegistry.ValidateProfiles` is called at startup (see section
5), profiles are loaded and validated before the first HTTP request arrives.

The result: if your config is broken, the application fails loudly at startup
with a clear error message. It never silently serves degraded responses.

---

## 8. Why Config-First Matters for Teams

The pattern has immediate practical benefits for any team building multi-agent
systems:

**Prompt diffs are reviewable in PRs.** When someone changes an agent's
instructions, the pull request diff shows a JSON change:

```diff
  "instructions": [
    "Build a coherent patient story across demographics, conditions, medications, observations, encounters, and allergies.",
    "Translate coded clinical facts into plain English.",
-   "Keep the structure stable across summaries."
+   "Keep the structure stable across summaries.",
+   "Include allergy severity when available."
  ],
```

This diff is readable by anyone. No C# knowledge required.

**Non-engineers can propose changes.** A product manager who wants the clinical
agent to include procedure dates can open `clinical.json`, add a line to
`instructions`, and submit a PR. They do not need to find the right service
class, understand the prompt assembly logic, or worry about breaking
compilation.

**Tool permissions are auditable.** A compliance review of "which agents can
trigger bulk exports?" is a simple search across JSON files for
`"bulk_export"`. The answer is immediately obvious: clinical and export. Not
lookup, not cohort. No code tracing needed.

**A/B testing prompts requires only file changes.** To test whether "lead with
the answer" or "start with reasoning" produces better clinical summaries, you
change `answerFirst` from `true` to `false` in the JSON file. The code path
is identical; only the config differs.

---

## 9. How to Apply This in Your Own Systems

The pattern generalizes beyond FHIR and beyond this specific tech stack. When
building any multi-agent system, follow these principles:

**Externalize everything that could change independently of orchestration
logic.** If you can imagine a product manager wanting to adjust it, it should
not be in a code file. This includes:
- System prompts and behavioral instructions
- Tool permissions per agent
- Response format expectations
- Routing rules and keyword hints

**Validate at startup.** Cross-reference every configuration value against the
set of known valid values. Typos in tool names, references to nonexistent agents
in the router, and malformed JSON should all be caught before the first request
arrives. The cost of a startup validation loop is negligible; the cost of a
production misconfig is not.

**Make it fail-fast.** A missing config directory, a malformed JSON file, or a
nameless agent profile should crash the application at boot, not produce a
`NullReferenceException` thirty minutes later when the right request path is
hit.

**Keep the composer pure.** The function that turns a config object into a
system prompt should have no dependencies, no side effects, and no conditional
logic based on runtime state. It takes a profile and returns a string. This
makes it trivially testable and easy to reason about.

**Use the type system for structure, not for behavior.** The `AgentProfile`
class defines the shape of a configuration. It does not contain methods that
act on that configuration. The behavior lives in `PromptComposer` (rendering),
`ToolRegistry` (filtering and validation), and `FileAgentProfileStore`
(loading). The config model is inert data.

The end result is a system where the question "what does this agent do?" is
always answerable by reading one JSON file, and where the answer "this config
is valid" is always verified before the system serves its first response.
