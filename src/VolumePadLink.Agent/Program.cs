using VolumePadLink.Agent.Configuration;
using VolumePadLink.Agent.IPC;
using VolumePadLink.Agent.Services;
using VolumePadLink.Agent.Services.Audio;
using VolumePadLink.Agent.Services.Device;
using VolumePadLink.Agent.Services.Display;
using VolumePadLink.Agent.Services.Feedback;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.Services.Leds;
using VolumePadLink.Agent.Services.Mappings;
using VolumePadLink.Agent.Services.Settings;
using VolumePadLink.Agent.Services.Target;
using VolumePadLink.Agent.State;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.AddSingleton<AgentStateStore>();
builder.Services.AddSingleton<IEventHub, EventHub>();
builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();

builder.Services.AddSingleton<AudioService>();
builder.Services.AddSingleton<IAudioService>(sp => sp.GetRequiredService<AudioService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<AudioService>());

builder.Services.AddSingleton<TargetService>();
builder.Services.AddSingleton<ITargetService>(sp => sp.GetRequiredService<TargetService>());

builder.Services.AddSingleton<IDeviceProtocolCodec, DeviceProtocolCodec>();
builder.Services.AddSingleton<IDeviceService, DeviceService>();

builder.Services.AddSingleton<IFramebufferRenderer, FramebufferRenderer>();
builder.Services.AddSingleton<IDirtyRegionTracker, DirtyRegionTracker>();
builder.Services.AddSingleton<IDisplayService, DisplayService>();
builder.Services.AddSingleton<ILedService, LedService>();

builder.Services.AddSingleton<IActionMappingService, ActionMappingService>();
builder.Services.AddHostedService(sp => (ActionMappingService)sp.GetRequiredService<IActionMappingService>());

builder.Services.AddSingleton<CommandRouter>();

builder.Services.AddSingleton<IIpcServer, PipeIpcServer>();
builder.Services.AddHostedService(sp => (PipeIpcServer)sp.GetRequiredService<IIpcServer>());

builder.Services.AddHostedService<StartupStateService>();
builder.Services.AddHostedService<FeedbackService>();

builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.SingleLine = true;
});

var host = builder.Build();
await host.RunAsync();

