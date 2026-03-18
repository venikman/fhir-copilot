using System.Text.Json;
using FhirCopilot.Api.Models;
using FhirCopilot.Api.Options;
using Microsoft.Extensions.Options;

namespace FhirCopilot.Api.Services;

public interface IAgentProfileStore
{
    AgentProfile GetAgent(string name);
    RouterProfile GetRouter();
    IReadOnlyDictionary<string, AgentProfile> GetAllAgents();
}

public sealed class FileAgentProfileStore : IAgentProfileStore
{
    private readonly Lazy<LoadedProfiles> _loaded;

    public FileAgentProfileStore(IHostEnvironment environment, IOptions<RuntimeOptions> runtimeOptions)
    {
        _loaded = new Lazy<LoadedProfiles>(() =>
        {
            var root = Path.Combine(environment.ContentRootPath, runtimeOptions.Value.AgentProfilesPath);

            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException($"Agent profiles path was not found: {root}");
            }

            var router = LoadFile<RouterProfile>(Path.Combine(root, "router.json"));
            var agents = Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(Path.GetFileName(path), "router.json", StringComparison.OrdinalIgnoreCase))
                .Select(LoadAgentFile)
                .ToDictionary(profile => profile.Name, StringComparer.OrdinalIgnoreCase);

            return new LoadedProfiles(router, agents);
        });
    }

    public AgentProfile GetAgent(string name)
    {
        if (_loaded.Value.Agents.TryGetValue(name, out var profile))
        {
            return profile;
        }

        throw new KeyNotFoundException($"No agent profile was found for '{name}'.");
    }

    public RouterProfile GetRouter() => _loaded.Value.Router;

    public IReadOnlyDictionary<string, AgentProfile> GetAllAgents() => _loaded.Value.Agents;

    private static AgentProfile LoadAgentFile(string path)
    {
        var profile = LoadFile<AgentProfile>(path);
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidDataException($"Agent profile at '{path}' does not declare a name.");
        }

        return profile;
    }

    private static T LoadFile<T>(string path)
    {
        var json = File.ReadAllText(path);
        var result = JsonSerializer.Deserialize<T>(json, JsonDefaults.Serializer);
        return result ?? throw new InvalidDataException($"Failed to deserialize '{path}' to {typeof(T).Name}.");
    }

    private sealed record LoadedProfiles(RouterProfile Router, Dictionary<string, AgentProfile> Agents);
}
