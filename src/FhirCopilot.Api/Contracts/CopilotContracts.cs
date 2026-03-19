namespace FhirCopilot.Api.Contracts;

public sealed record CopilotRequest(string Query, string? ThreadId = null);

public sealed record Citation(string ResourceId, string? Label = null);

public sealed record CopilotResponse(
    string Answer,
    IReadOnlyList<Citation> Citations,
    IReadOnlyList<string> Reasoning,
    IReadOnlyList<string> ToolsUsed,
    string AgentUsed,
    string Confidence,
    string ThreadId,
    bool IsStub);

public sealed record CopilotStreamEvent(
    string Type,
    string? AgentType = null,
    string? ThreadId = null,
    string? Content = null,
    CopilotResponse? Response = null,
    string? Message = null,
    bool IsStub = false)
{
    public static CopilotStreamEvent Meta(string agentType, string threadId, bool isStub)
        => new("meta", AgentType: agentType, ThreadId: threadId, IsStub: isStub);

    public static CopilotStreamEvent Delta(string content)
        => new("delta", Content: content);

    public static CopilotStreamEvent Done(CopilotResponse response)
        => new("done", Response: response);

    public static CopilotStreamEvent Error(string message)
        => new("error", Message: message);
}
