using System.Text.Json;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Configuration;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Settings;

public sealed class JsonSettingsStore(IOptions<AgentOptions> options, ILogger<JsonSettingsStore> logger) : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<StoredAgentSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            return Default();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<StoredAgentSettings>(stream, JsonOptions, cancellationToken);
            return stored ?? Default();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load settings from {Path}. Using defaults.", path);
            return Default();
        }
    }

    public async Task SaveAsync(StoredAgentSettings settings, CancellationToken cancellationToken = default)
    {
        var path = GetSettingsPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    private string GetSettingsPath()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.SettingsPath))
        {
            return options.Value.SettingsPath;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "VolumePadLink", "settings.json");
    }

    private static StoredAgentSettings Default()
    {
        return new StoredAgentSettings(
            new ActiveTargetDto(TargetKinds.Master, null, null),
            new DeviceSettingsDto(24, 0.65f, 0.4f, 0.8f, 0.8f, false, 450),
            AudioMode.Real,
            null);
    }
}

