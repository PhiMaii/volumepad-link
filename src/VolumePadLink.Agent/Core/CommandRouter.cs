using System.Text.Json;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Core;

public sealed class CommandRouter(
    ILogger<CommandRouter> logger,
    Services.IDeviceService deviceService,
    Services.IAudioService audioService,
    Services.ISettingsService settingsService,
    Services.IDebugService debugService) : ICommandRouter
{
    public async Task<ProtocolEnvelope> HandleAsync(ProtocolEnvelope request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.V != ProtocolConstants.Version)
            {
                throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, $"Unsupported protocol version {request.V}.");
            }

            if (request.Type != ProtocolMessageType.Request)
            {
                throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "Only request messages are accepted.");
            }

            return request.Name switch
            {
                ProtocolNames.Methods.DeviceListPorts => SuccessResponse(request, await deviceService.ListPortsAsync(cancellationToken)),
                ProtocolNames.Methods.DeviceConnect => SuccessResponse(request, await deviceService.ConnectAsync(ReadDeviceConnectRequest(request).PortName, cancellationToken)),
                ProtocolNames.Methods.DeviceDisconnect => SuccessResponse(request, await deviceService.DisconnectAsync(cancellationToken)),
                ProtocolNames.Methods.DeviceReconnect => SuccessResponse(request, await deviceService.ReconnectAsync(cancellationToken)),
                ProtocolNames.Methods.DeviceGetStatus => SuccessResponse(request, await deviceService.GetStatusAsync(cancellationToken)),

                ProtocolNames.Methods.AudioMasterGet => SuccessResponse(request, await audioService.GetMasterAsync(cancellationToken)),
                ProtocolNames.Methods.AudioMasterSetVolume => SuccessResponse(request, await audioService.SetVolumeAsync(ReadSetVolumeRequest(request).Volume, cancellationToken)),
                ProtocolNames.Methods.AudioMasterSetMute => SuccessResponse(request, await audioService.SetMuteAsync(ReadSetMuteRequest(request).Muted, cancellationToken)),
                ProtocolNames.Methods.AudioMasterToggleMute => SuccessResponse(request, await audioService.ToggleMuteAsync(cancellationToken)),

                ProtocolNames.Methods.SettingsGet => SuccessResponse(request, new SettingsGetResponse(settingsService.GetEffectiveSettings())),
                ProtocolNames.Methods.SettingsUpdate => SuccessResponse(request, await settingsService.ApplyAsync(ReadSettingsUpdateRequest(request), cancellationToken)),

                ProtocolNames.Methods.DebugGetState => SuccessResponse(request, await debugService.GetStateAsync(cancellationToken)),
                ProtocolNames.Methods.DebugApplyTuning => SuccessResponse(request, await debugService.ApplyTuningAsync(ReadDebugTuningRequest(request), cancellationToken)),
                ProtocolNames.Methods.DebugSetStream => await HandleDebugSetStreamAsync(request, cancellationToken),

                ProtocolNames.Methods.ServiceRestartAudioBackend => SuccessResponse(
                    request,
                    await RestartAudioBackendAsync(cancellationToken)),

                _ => ErrorResponse(request, ProtocolNames.ErrorCodes.UnknownMethod, $"Unknown method '{request.Name}'."),
            };
        }
        catch (ProtocolException ex)
        {
            return ErrorResponse(request, ex.Code, ex.Message);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid payload for request {Method}", request.Name);
            return ErrorResponse(request, ProtocolNames.ErrorCodes.InvalidPayload, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled command router error for {Method}", request.Name);
            return ErrorResponse(request, ProtocolNames.ErrorCodes.InternalError, ex.Message);
        }
    }

    private static ProtocolEnvelope SuccessResponse(ProtocolEnvelope request, object payload)
    {
        return new ProtocolEnvelope
        {
            Type = ProtocolMessageType.Response,
            V = ProtocolConstants.Version,
            Id = request.Id,
            Name = request.Name,
            Ok = true,
            Payload = ProtocolJson.ToElement(payload),
        };
    }

    private static ProtocolEnvelope ErrorResponse(ProtocolEnvelope request, string code, string message)
    {
        return new ProtocolEnvelope
        {
            Type = ProtocolMessageType.Response,
            V = ProtocolConstants.Version,
            Id = request.Id,
            Name = request.Name,
            Ok = false,
            Error = new ProtocolError(code, message),
            Payload = ProtocolJson.EmptyObject,
        };
    }

    private static DeviceConnectRequest ReadDeviceConnectRequest(ProtocolEnvelope request)
    {
        return ProtocolJson.DeserializePayload<DeviceConnectRequest>(request);
    }

    private static SetVolumeRequest ReadSetVolumeRequest(ProtocolEnvelope request)
    {
        return ProtocolJson.DeserializePayload<SetVolumeRequest>(request);
    }

    private static SetMuteRequest ReadSetMuteRequest(ProtocolEnvelope request)
    {
        return ProtocolJson.DeserializePayload<SetMuteRequest>(request);
    }

    private static AppSettings ReadSettingsUpdateRequest(ProtocolEnvelope request)
    {
        if (request.Payload.ValueKind == JsonValueKind.Object && request.Payload.TryGetProperty("settings", out var settingsElement))
        {
            return settingsElement.Deserialize<AppSettings>(ProtocolJson.SerializerOptions)
                ?? throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "settings payload is required.");
        }

        return ProtocolJson.DeserializePayload<AppSettings>(request);
    }

    private static DebugTuning ReadDebugTuningRequest(ProtocolEnvelope request)
    {
        if (request.Payload.ValueKind == JsonValueKind.Object && request.Payload.TryGetProperty("tuning", out var tuningElement))
        {
            return tuningElement.Deserialize<DebugTuning>(ProtocolJson.SerializerOptions)
                ?? throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "tuning payload is required.");
        }

        return ProtocolJson.DeserializePayload<DebugTuning>(request);
    }

    private static DebugSetStreamRequest ReadDebugSetStreamRequest(ProtocolEnvelope request)
    {
        return ProtocolJson.DeserializePayload<DebugSetStreamRequest>(request);
    }

    private async Task<ServiceRestartAudioBackendResponse> RestartAudioBackendAsync(CancellationToken cancellationToken)
    {
        await audioService.RestartBackendAsync(cancellationToken);
        return new ServiceRestartAudioBackendResponse(true);
    }

    private async Task<ProtocolEnvelope> HandleDebugSetStreamAsync(ProtocolEnvelope request, CancellationToken cancellationToken)
    {
        var stream = ReadDebugSetStreamRequest(request);
        var result = await debugService.SetStreamAsync(stream.Enabled, stream.IntervalMs, cancellationToken);
        return SuccessResponse(request, result);
    }
}
