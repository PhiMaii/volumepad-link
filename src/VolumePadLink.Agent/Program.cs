using VolumePadLink.Agent.Core;
using VolumePadLink.Agent.Options;
using VolumePadLink.Agent.Persistence;
using VolumePadLink.Agent.Services;
using VolumePadLink.Agent.Services.Audio;
using VolumePadLink.Agent.Services.Ring;
using VolumePadLink.Agent.Services.StreamDeck;
using VolumePadLink.Agent.Services.Tray;
using VolumePadLink.Agent.Transport.Device;
using VolumePadLink.Agent.Transport.Ipc;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<AgentRuntimeOptions>()
    .Bind(builder.Configuration.GetSection(AgentRuntimeOptions.SectionName));

builder.Services
    .AddOptions<QueueOptions>()
    .Bind(builder.Configuration.GetSection(QueueOptions.SectionName));

builder.Services.AddSingleton<RuntimeStateStore>();
builder.Services.AddSingleton<EventBus>();
builder.Services.AddSingleton<ICommandRouter, CommandRouter>();

builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();
builder.Services.AddSingleton<IDeviceLinkFactory, DeviceLinkFactory>();

builder.Services.AddSingleton<WasapiAudioBackend>();
builder.Services.AddSingleton<SimulatedAudioBackend>();
builder.Services.AddSingleton<IAudioBackend, ResilientAudioBackend>();

builder.Services.AddSingleton<DeviceService>();
builder.Services.AddSingleton<IDeviceService>(sp => sp.GetRequiredService<DeviceService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceService>());

builder.Services.AddSingleton<RingRenderService>();
builder.Services.AddSingleton<IRingRenderService>(sp => sp.GetRequiredService<RingRenderService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RingRenderService>());

builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddSingleton<IDebugService, DebugService>();
builder.Services.AddSingleton<IAudioService, AudioService>();
builder.Services.AddSingleton<IStreamDeckStateProvider, StreamDeckStateProvider>();
builder.Services.AddSingleton<IStreamDeckCommandService, StreamDeckCommandService>();
builder.Services.AddSingleton<StreamDeckTrafficMonitor>();

builder.Services.AddHostedService<FixedInputService>();
builder.Services.AddHostedService<MeterLoopService>();
builder.Services.AddHostedService<IpcServerService>();
builder.Services.AddHostedService<StartupCoordinatorService>();
builder.Services.AddHostedService<StreamDeckEndpointService>();
builder.Services.AddHostedService<TrayService>();

var host = builder.Build();
host.Run();
