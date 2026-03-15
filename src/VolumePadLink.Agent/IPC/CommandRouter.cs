using System.Reflection;
using System.Text.Json;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.State;
using VolumePadLink.Contracts.Abstractions;
using VolumePadLink.Contracts.Commands;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.IPC;

public sealed class CommandRouter(
    IAudioService audioService,
    ITargetService targetService,
    IDeviceService deviceService,
    ISettingsStore settingsStore,
    AgentStateStore stateStore,
    IEventHub eventHub,
    ILogger<CommandRouter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IpcMessage> HandleAsync(IpcMessage command, CancellationToken cancellationToken)
    {
        var responseId = string.IsNullOrWhiteSpace(command.Id) ? Guid.NewGuid().ToString("N") : command.Id;

        try
        {
            return command.Name switch
            {
                CommandNames.AppPing => CreateResponse(responseId, command.Name, new PingResponse(GetVersion(), DateTimeOffset.UtcNow)),

                CommandNames.AudioGetGraph => CreateResponse(responseId, command.Name, new AudioGraphResponse(await audioService.GetGraphAsync(cancellationToken))),
                CommandNames.AudioSetMasterVolume => await HandleSetMasterVolumeAsync(command, responseId, cancellationToken),
                CommandNames.AudioSetMasterMute => await HandleSetMasterMuteAsync(command, responseId, cancellationToken),
                CommandNames.AudioSetSessionVolume => await HandleSetSessionVolumeAsync(command, responseId, cancellationToken),
                CommandNames.AudioSetSessionMute => await HandleSetSessionMuteAsync(command, responseId, cancellationToken),

                CommandNames.TargetGetActive => CreateResponse(responseId, command.Name, new TargetResponse(await targetService.GetActiveTargetAsync(cancellationToken))),
                CommandNames.TargetSelect => await HandleTargetSelectAsync(command, responseId, cancellationToken),

                CommandNames.DeviceGetStatus => CreateResponse(responseId, command.Name, new DeviceStatusResponse(await deviceService.GetStatusAsync(cancellationToken))),
                CommandNames.DeviceConnect => await HandleDeviceConnectAsync(command, responseId, cancellationToken),
                CommandNames.DeviceDisconnect => CreateResponse(responseId, command.Name, new DeviceStatusResponse(await deviceService.DisconnectAsync(cancellationToken))),
                CommandNames.DeviceGetCapabilities => CreateResponse(responseId, command.Name, new DeviceCapabilitiesResponse(await deviceService.GetCapabilitiesAsync(cancellationToken))),

                CommandNames.SettingsGet => CreateResponse(responseId, command.Name, new SettingsResponse(stateStore.GetAppSettings())),
                CommandNames.SettingsUpdate => await HandleSettingsUpdateAsync(command, responseId, cancellationToken),

                _ => CreateError(responseId, command.Name, $"Unknown command '{command.Name}'.")
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Command handling failed for {CommandName}", command.Name);
            return CreateError(responseId, command.Name, ex.Message);
        }
    }

    private async Task<IpcMessage> HandleSetMasterVolumeAsync(IpcMessage command, string responseId, CancellationToken cancellationToken)
    {
        var request = DeserializePayload<SetMasterVolumeRequest>(command.Payload);
        await audioService.SetMasterVolumeAsync(request.Value, cancellationToken);
        return CreateResponse(responseId, command.Name, new AckResponse(true));
    }

    private async Task<IpcMessage> HandleSetMasterMuteAsync(IpcMessage command, string responseId, CancellationToken cancellationToken)
    {
        var request = DeserializePayload<SetMasterMuteRequest>(command.Payload);
        await audioService.SetMasterMuteAsync(request.Muted, cancellationToken);
        return CreateResponse(responseId, command.Name, new AckResponse(true));
    }

    private async Task<IpcMessage> HandleSetSessionVolumeAsync(IpcMessage command, string responseId, CancellationToken cancellationToken)
    {
        var request = DeserializePayload<SetSessionVolumeRequest>(command.Payload);
        await audioService.SetSessionVolumeAsync(request.SessionId, request.Value, cancellationToken);
        return CreateResponse(responseId, command.Name, new AckResponse(true));
    }

    private async Task<IpcMessage> HandleSetSessionMuteAsync(IpcMessage command, string responseId, CancellationToken cancellationToken)
    {
        var request = DeserializePayload<SetSessionMuteRequest>(command.Payload);
        await audioService.SetSessionMuteAsync(request.SessionId, request.Muted, cancellationToken);
        return CreateResponse(responseId, command.Name, new AckResponse(true));
    }

    private async Task<IpcMessage> HandleTargetSelectAsync(IpcMessage command, string responseId, CancellationToken cancellationToken)
    {
        var request = DeserializePayload<SelectTargetRequest>(command.Payload);
        var selected = await targetService.SelectTargetAsync(request.Target, cancellationToken);
        return CreateResponse(responseId, command.Name, new TargetResponse(selected));
    }

    private async Task<IpcMessage> HandleDeviceConnectAsync(IpcMessage command, string responseId, CancellationToken cancellationToken)
    {
        var request = DeserializePayload<ConnectDeviceRequest>(command.Payload);
        var status = await deviceService.ConnectAsync(request.PortName, cancellationToken);
        await PersistCurrentStateAsync(cancellationToken);
        return CreateResponse(responseId, command.Name, new DeviceStatusResponse(status));
    }

    private async Task<IpcMessage> HandleSettingsUpdateAsync(IpcMessage command, string responseId, CancellationToken cancellationToken)
    {
        var request = DeserializePayload<UpdateSettingsRequest>(command.Payload);

        stateStore.SetAppSettings(request.Settings);

        await audioService.SetModeAsync(request.Settings.AudioMode, cancellationToken);
        await deviceService.ApplySettingsAsync(request.Settings.Device, cancellationToken);
        await PersistCurrentStateAsync(cancellationToken);

        await eventHub.PublishAsync(EventNames.DeviceSettingsApplied, new DeviceSettingsAppliedEvent(request.Settings.Device), cancellationToken);
        return CreateResponse(responseId, command.Name, new SettingsResponse(stateStore.GetAppSettings()));
    }

    private async Task PersistCurrentStateAsync(CancellationToken cancellationToken)
    {
        await settingsStore.SaveAsync(new StoredAgentSettings(
            stateStore.GetActiveTarget(),
            stateStore.GetSettings(),
            stateStore.GetAudioMode(),
            stateStore.GetDeviceStatus().PortName), cancellationToken);
    }

    private static T DeserializePayload<T>(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            var instance = Activator.CreateInstance<T>();
            if (instance is null)
            {
                throw new InvalidOperationException($"Payload missing for type {typeof(T).Name}.");
            }

            return instance;
        }

        var typed = JsonSerializer.Deserialize<T>(payload.GetRawText(), JsonOptions);
        if (typed is null)
        {
            throw new InvalidOperationException($"Unable to parse payload as {typeof(T).Name}.");
        }

        return typed;
    }

    private static string GetVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
    }

    private static IpcMessage CreateResponse(string? id, string name, object payload)
    {
        return new IpcMessage
        {
            Type = IpcMessageKinds.Response,
            Id = id,
            Name = name,
            Payload = JsonSerializer.SerializeToElement(payload, JsonOptions)
        };
    }

    private static IpcMessage CreateError(string? id, string name, string error)
    {
        return new IpcMessage
        {
            Type = IpcMessageKinds.Response,
            Id = id,
            Name = name,
            Payload = JsonSerializer.SerializeToElement(new AckResponse(false, error), JsonOptions),
            Error = error
        };
    }
}
