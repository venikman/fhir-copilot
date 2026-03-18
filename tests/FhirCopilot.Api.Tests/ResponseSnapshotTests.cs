using System.Net.Http.Json;

namespace FhirCopilot.Api.Tests;

public class ResponseSnapshotTests : CopilotFixture
{
    public ResponseSnapshotTests(CopilotFixture.StubFactory factory) : base(factory) { }

    [Fact]
    public async Task Clinical_summary()
    {
        var body = await PostAndReadAsync("Clinical summary for patient-0001", "snap-clinical");
        await Verifier.Verify(body).ScrubMember("threadId");
    }

    [Fact]
    public async Task Lookup_coverage()
    {
        var body = await PostAndReadAsync("What insurance does patient-0001 have?", "snap-lookup");
        await Verifier.Verify(body).ScrubMember("threadId");
    }

    [Fact]
    public async Task Analytics_diabetic_count()
    {
        var body = await PostAndReadAsync("How many diabetic patients do we have?", "snap-analytics");
        await Verifier.Verify(body).ScrubMember("threadId");
    }

    [Fact]
    public async Task Cohort_care_gap()
    {
        var body = await PostAndReadAsync("Which diabetic patients are without metformin?", "snap-cohort");
        await Verifier.Verify(body).ScrubMember("threadId");
    }

    [Fact]
    public async Task Export_bulk()
    {
        var body = await PostAndReadAsync("Export all data for the Northwind group", "snap-export");
        await Verifier.Verify(body).ScrubMember("threadId");
    }

    [Fact]
    public async Task Search_patients()
    {
        var body = await PostAndReadAsync("Find patients named Carter", "snap-search");
        await Verifier.Verify(body).ScrubMember("threadId");
    }

    private async Task<string> PostAndReadAsync(string query, string threadId)
    {
        var response = await Client.PostAsJsonAsync("/api/copilot",
            new { query, threadId });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
