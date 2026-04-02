using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Options;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Persistence;

public sealed class JsonSettingsStore(
    IOptions<AgentRuntimeOptions> runtimeOptions,
    ILogger<JsonSettingsStore> logger) : ISettingsStore
{
    private const string FileName = "settings.json";

    public async Task<AppSettings?> LoadAsync(CancellationToken cancellationToken)
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, ProtocolJson.SerializerOptions, cancellationToken);
        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var path = GetSettingsPath();
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Missing settings directory.");
        Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(settings, ProtocolJson.SerializerOptions);
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken);

        File.Move(tempPath, path, overwrite: true);
        logger.LogInformation("Persisted settings to {Path}", path);
    }

    private string GetSettingsPath()
    {
        var configuredRoot = runtimeOptions.Value.DataDirectory;
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VolumePadLink")
            : configuredRoot;

        return Path.Combine(root, FileName);
    }
}
