using System.Net.Http.Json;
using System.Text.Json;

namespace FhirCopilot.Api.Tests;

public class ResponseEnvelopeTests : CopilotFixture
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ResponseEnvelopeTests(CopilotFixture.StubFactory factory) : base(factory) { }

    [Fact]
    public async Task ThreadId_is_preserved_when_provided()
    {
        var response = await Client.PostAsJsonAsync("/api/copilot",
            new { query = "How many diabetic patients?", threadId = "my-custom-thread" });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ThreadDto>(body, JsonOptions);

        Assert.Equal("my-custom-thread", result!.ThreadId);
    }

    [Fact]
    public async Task ThreadId_is_generated_when_missing()
    {
        var response = await Client.PostAsJsonAsync("/api/copilot",
            new { query = "How many patients?" });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ThreadDto>(body, JsonOptions);

        Assert.False(string.IsNullOrWhiteSpace(result!.ThreadId));
    }

    [Fact]
    public async Task Response_is_marked_as_stub()
    {
        var response = await Client.PostAsJsonAsync("/api/copilot",
            new { query = "Clinical summary for patient-0001", threadId = "stub-check" });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        Assert.True(doc.RootElement.GetProperty("isStub").GetBoolean());
    }

    private sealed record ThreadDto(string ThreadId);
}
