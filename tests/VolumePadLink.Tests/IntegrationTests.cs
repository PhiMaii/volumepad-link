using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Core;
using VolumePadLink.Agent.Options;
using VolumePadLink.Agent.Persistence;
using VolumePadLink.Agent.Services;
using VolumePadLink.Agent.Transport.Device;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Tests;

public sealed class IntegrationTests
{
    [Fact]
    public async Task DeviceService_SimulatorConnectDisconnectReconnect_Works()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        var runtimeOptions = Options.Create(new AgentRuntimeOptions
        {
            SimulatorPortName = "SIMULATED",
            DefaultAutoConnectPort = "SIMULATED",
            DeviceRequestTimeoutMs = 800,
            AutoReconnectDelayMs = 200,
        });
        var queueOptions = Options.Create(new QueueOptions());
        var linkFactory = new DeviceLinkFactory(runtimeOptions, loggerFactory);
        var stateStore = new RuntimeStateStore();
        var eventBus = new EventBus(queueOptions);
        var service = new DeviceService(
            loggerFactory.CreateLogger<DeviceService>(),
            runtimeOptions,
            queueOptions,
            linkFactory,
            stateStore,
            eventBus);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var connected = await service.ConnectAsync("SIMULATED", CancellationToken.None);
            Assert.Equal(ConnectionState.Connected, connected.ConnectionState);
            Assert.Equal("SIMULATED", connected.PortName);

            var disconnected = await service.DisconnectAsync(CancellationToken.None);
            Assert.Equal(ConnectionState.Disconnected, disconnected.ConnectionState);

            var reconnected = await service.ReconnectAsync(CancellationToken.None);
            Assert.Equal(ConnectionState.Connected, reconnected.ConnectionState);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }
    }

    [Fact]
    public async Task SettingsService_Apply_PersistsAndForwardsEffectiveValues()
    {
        var inMemoryStore = new InMemorySettingsStore();
        var stateStore = new RuntimeStateStore();
        var queueOptions = Options.Create(new QueueOptions());
        var eventBus = new EventBus(queueOptions);
        var fakeDeviceService = new FakeDeviceService();
        var logger = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None)).CreateLogger<SettingsService>();
        var settingsService = new SettingsService(inMemoryStore, stateStore, eventBus, fakeDeviceService, logger);

        await settingsService.InitializeAsync(CancellationToken.None);

        var input = new AppSettings
        {
            MeterMode = MeterModes.PeakIndicator,
            VolumeStepSize = 0.04,
            LowEndstopPosition = -0.9,
            HighEndstopPosition = 0.9,
        };

        var response = await settingsService.ApplyAsync(input, CancellationToken.None);

        Assert.Equal(MeterModes.PeakIndicator, response.Effective.MeterMode);
        Assert.Equal(0.04, response.Effective.VolumeStepSize, 6);
        Assert.NotNull(inMemoryStore.LastSaved);
        Assert.Equal(MeterModes.PeakIndicator, inMemoryStore.LastSaved!.MeterMode);
        Assert.Contains(fakeDeviceService.QueuedCommands, command => command.Name == ProtocolNames.DeviceMethods.DeviceApplySettings);
    }

    [Fact]
    public async Task DebugService_ApplyAndReadback_RoundTripsWithSimulator()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        var runtimeOptions = Options.Create(new AgentRuntimeOptions
        {
            SimulatorPortName = "SIMULATED",
            DefaultAutoConnectPort = "SIMULATED",
            DeviceRequestTimeoutMs = 1200,
        });
        var queueOptions = Options.Create(new QueueOptions());
        var linkFactory = new DeviceLinkFactory(runtimeOptions, loggerFactory);
        var stateStore = new RuntimeStateStore();
        var eventBus = new EventBus(queueOptions);
        var deviceService = new DeviceService(
            loggerFactory.CreateLogger<DeviceService>(),
            runtimeOptions,
            queueOptions,
            linkFactory,
            stateStore,
            eventBus);

        await deviceService.StartAsync(CancellationToken.None);
        await deviceService.ConnectAsync("SIMULATED", CancellationToken.None);

        try
        {
            var debugService = new DebugService(
                deviceService,
                stateStore,
                eventBus,
                loggerFactory.CreateLogger<DebugService>());

            var updated = await debugService.ApplyTuningAsync(new DebugTuning
            {
                DetentStrengthMaxVPerRad = 3.0,
                SnapStrengthMaxVPerRad = 1.5,
                ClickPulseVoltage = 1.4,
                ClickPulseMs = 28,
                EndstopMinPos = -1.0,
                EndstopMaxPos = 1.0,
                EndstopMinStrength = 0.7,
                EndstopMaxStrength = 0.7,
            }, CancellationToken.None);

            Assert.Equal("volumepad-001", updated.DeviceId);
            Assert.InRange(updated.DetentStrength, 0.99, 1.0);
            Assert.InRange(updated.SnapStrength, 0.49, 0.51);

            var streamed = await debugService.SetStreamAsync(true, 150, CancellationToken.None);
            Assert.Equal("volumepad-001", streamed.DeviceId);
        }
        finally
        {
            await deviceService.StopAsync(CancellationToken.None);
            deviceService.Dispose();
        }
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        public AppSettings? LastSaved { get; private set; }

        public Task<AppSettings?> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<AppSettings?>(null);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            LastSaved = settings.Clone();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeviceService : IDeviceService
    {
        public List<(string Name, object Payload, DeviceCommandPriority Priority)> QueuedCommands { get; } = [];

        public event EventHandler<DeviceButtonInput>? ButtonInputReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<DeviceEncoderInput>? EncoderInputReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<DebugState>? DebugStateReceived
        {
            add { }
            remove { }
        }

        public Task<DeviceListPortsResponse> ListPortsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeviceListPortsResponse([new DevicePortInfo("SIMULATED", false)]));
        }

        public Task<DeviceStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeviceStatus(ConnectionState.Disconnected, null, null, null, null));
        }

        public Task<DeviceStatus> ConnectAsync(string portName, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeviceStatus(ConnectionState.Connected, portName, "volumepad-001", "2.0.0", DateTimeOffset.UtcNow));
        }

        public Task<DeviceStatus> DisconnectAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeviceStatus(ConnectionState.Disconnected, null, null, null, DateTimeOffset.UtcNow));
        }

        public Task<DeviceStatus> ReconnectAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeviceStatus(ConnectionState.Connected, "SIMULATED", "volumepad-001", "2.0.0", DateTimeOffset.UtcNow));
        }

        public Task QueueCommandAsync(string name, object payload, DeviceCommandPriority priority, CancellationToken cancellationToken)
        {
            QueuedCommands.Add((name, payload, priority));
            return Task.CompletedTask;
        }

        public Task<TResponse> SendRequestAsync<TResponse>(string name, object payload, DeviceCommandPriority priority, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("FakeDeviceService does not implement request/response calls for this test.");
        }
    }
}
