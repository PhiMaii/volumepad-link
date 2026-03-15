using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Configuration;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.Services.Settings;
using VolumePadLink.Contracts.DTOs;
using Xunit;

namespace VolumePadLink.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task JsonSettingsStore_RoundTripsAudioMode()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VolumePadLink.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var options = Options.Create(new AgentOptions { SettingsPath = settingsPath });
        var store = new JsonSettingsStore(options, NullLogger<JsonSettingsStore>.Instance);

        var saved = new StoredAgentSettings(
            new ActiveTargetDto(TargetKinds.Master, null, null),
            new DeviceSettingsDto(32, 0.8f, 0.5f, 0.7f, 0.6f, true, 500),
            AudioMode.Simulated,
            "sim");

        await store.SaveAsync(saved);
        var loaded = await store.LoadAsync();

        Assert.Equal(AudioMode.Simulated, loaded.AudioMode);
        Assert.Equal(saved.DeviceSettings.DetentCount, loaded.DeviceSettings.DetentCount);
        Assert.Equal(saved.PreferredDevicePort, loaded.PreferredDevicePort);

        Directory.Delete(tempRoot, recursive: true);
    }
}
