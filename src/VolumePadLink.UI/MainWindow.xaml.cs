using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using VolumePadLink.Contracts.Abstractions;
using VolumePadLink.Contracts.Commands;
using VolumePadLink.Contracts.DTOs;
using VolumePadLink.UI.Models;
using VolumePadLink.UI.Services;

namespace VolumePadLink.UI;

public sealed partial class MainWindow : Window
{
    [Flags]
    private enum PendingRefresh
    {
        None = 0,
        Device = 1,
        Target = 2,
        Settings = 4
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan EventRefreshDebounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RealtimeVolumeSendDebounce = TimeSpan.FromMilliseconds(35);

    private readonly BackendClient _backendClient = new();
    private readonly CancellationTokenSource _eventLoopCts = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly ObservableCollection<AudioSessionItem> _sessionItems = [];
    private readonly Dictionary<string, AudioSessionItem> _sessionItemsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _sessionVolumeDebounce = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _volumeDebounceSync = new();

    private Task? _eventLoopTask;
    private CancellationTokenSource? _masterVolumeDebounceCts;
    private bool _masterMuted;
    private float _lastMasterVolume;
    private bool _suppressVolumeValueChanged;
    private IReadOnlyList<AudioSessionDto> _lastSessions = [];
    private DateTimeOffset _lastEventRefreshUtc = DateTimeOffset.MinValue;
    private int _pendingRefreshFlags;
    private int _eventRefreshScheduled;

    public MainWindow()
    {
        InitializeComponent();

        SessionsListView.ItemsSource = _sessionItems;

        _backendClient.EventReceived += OnBackendEventAsync;
        Closed += OnClosed;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _eventLoopTask = _backendClient.RunEventLoopAsync(_eventLoopCts.Token);
        await RefreshAsync();
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _eventLoopCts.Cancel();
        CancelPendingVolumeSends();

        if (_eventLoopTask is not null)
        {
            try
            {
                await _eventLoopTask;
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during shutdown.
            }
        }

        _refreshGate.Dispose();
    }

    private Task OnBackendEventAsync(IpcMessage message)
    {
        if (TryQueueAudioEventUpdate(message))
        {
            return Task.CompletedTask;
        }

        var flags = MapEventToRefresh(message.Name);
        if (flags == PendingRefresh.None)
        {
            return Task.CompletedTask;
        }

        Interlocked.Or(ref _pendingRefreshFlags, (int)flags);

        if (Interlocked.Exchange(ref _eventRefreshScheduled, 1) == 1)
        {
            return Task.CompletedTask;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = ProcessPendingRefreshesAsync());
        return Task.CompletedTask;
    }

    private bool TryQueueAudioEventUpdate(IpcMessage message)
    {
        switch (message.Name)
        {
            case EventNames.AudioMasterChanged:
                if (!TryDeserializePayload<AudioMasterChangedEvent>(message, out var masterEvent))
                {
                    return true;
                }

                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => ApplyMaster(masterEvent.Master));
                return true;

            case EventNames.AudioSessionAdded:
            case EventNames.AudioSessionUpdated:
                if (!TryDeserializePayload<AudioSessionChangedEvent>(message, out var sessionEvent))
                {
                    return true;
                }

                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => UpsertSession(sessionEvent.Session));
                return true;

            case EventNames.AudioSessionRemoved:
                if (!TryDeserializePayload<AudioSessionRemovedEvent>(message, out var removedEvent))
                {
                    return true;
                }

                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => RemoveSession(removedEvent.SessionId));
                return true;

            case EventNames.AudioGraphChanged:
                // Ignore graph-wide events to avoid full UI redraw on single audio changes.
                return true;

            default:
                return false;
        }
    }

    private static PendingRefresh MapEventToRefresh(string eventName)
    {
        return eventName switch
        {
            EventNames.TargetActiveChanged => PendingRefresh.Target,
            EventNames.DeviceConnected or EventNames.DeviceDisconnected => PendingRefresh.Device,
            EventNames.DeviceSettingsApplied => PendingRefresh.Settings,
            _ => PendingRefresh.None
        };
    }

    private static bool TryDeserializePayload<T>(IpcMessage message, [NotNullWhen(true)] out T? payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<T>(message.Payload.GetRawText(), JsonOptions);
            return payload is not null;
        }
        catch
        {
            payload = default;
            return false;
        }
    }

    private async Task ProcessPendingRefreshesAsync()
    {
        try
        {
            while (true)
            {
                await _refreshGate.WaitAsync();
                try
                {
                    var elapsed = DateTimeOffset.UtcNow - _lastEventRefreshUtc;
                    if (elapsed < EventRefreshDebounce)
                    {
                        await Task.Delay(EventRefreshDebounce - elapsed);
                    }

                    var flags = (PendingRefresh)Interlocked.Exchange(ref _pendingRefreshFlags, 0);
                    if (flags == PendingRefresh.None)
                    {
                        return;
                    }

                    if ((flags & PendingRefresh.Target) != 0)
                    {
                        await RefreshTargetAsync();
                    }

                    if ((flags & PendingRefresh.Device) != 0)
                    {
                        await RefreshDeviceAsync();
                    }

                    if ((flags & PendingRefresh.Settings) != 0)
                    {
                        await RefreshSettingsAsync();
                    }

                    _lastEventRefreshUtc = DateTimeOffset.UtcNow;
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Event refresh failed: {ex.Message}";
                }
                finally
                {
                    _refreshGate.Release();
                }

                if (Volatile.Read(ref _pendingRefreshFlags) == 0)
                {
                    return;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _eventRefreshScheduled, 0);

            if (Volatile.Read(ref _pendingRefreshFlags) != 0 && Interlocked.Exchange(ref _eventRefreshScheduled, 1) == 0)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = ProcessPendingRefreshesAsync());
            }
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            await RefreshAgentAsync();
            await RefreshDeviceAsync();
            await RefreshTargetAsync();
            await RefreshAudioAsync();
            await RefreshSettingsAsync();
            StatusText.Text = "Refreshed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Refresh failed: {ex.Message}";
        }
    }

    private async Task RefreshAgentAsync()
    {
        var ping = await _backendClient.SendCommandAsync<EmptyRequest, PingResponse>(CommandNames.AppPing, new EmptyRequest());
        AgentStatusText.Text = $"Connected. Agent {ping.Version} at {ping.UtcNow:HH:mm:ss} UTC";
    }

    private async Task RefreshDeviceAsync()
    {
        var deviceStatus = await _backendClient.SendCommandAsync<EmptyRequest, DeviceStatusResponse>(CommandNames.DeviceGetStatus, new EmptyRequest());
        ApplyDeviceStatus(deviceStatus.Status);
    }

    private async Task RefreshTargetAsync()
    {
        var target = await _backendClient.SendCommandAsync<EmptyRequest, TargetResponse>(CommandNames.TargetGetActive, new EmptyRequest());
        ApplyTarget(target.Target);
    }

    private async Task RefreshAudioAsync()
    {
        var graphResponse = await _backendClient.SendCommandAsync<EmptyRequest, AudioGraphResponse>(CommandNames.AudioGetGraph, new EmptyRequest());
        ApplyGraph(graphResponse.Graph);
    }

    private async Task RefreshSettingsAsync()
    {
        var settingsResponse = await _backendClient.SendCommandAsync<EmptyRequest, SettingsResponse>(CommandNames.SettingsGet, new EmptyRequest());
        var settings = settingsResponse.Settings;

        DetentCountTextBox.Text = settings.Device.DetentCount.ToString(CultureInfo.InvariantCulture);
        DetentStrengthTextBox.Text = settings.Device.DetentStrength.ToString("0.00", CultureInfo.InvariantCulture);
        LedBrightnessTextBox.Text = settings.Device.LedBrightness.ToString("0.00", CultureInfo.InvariantCulture);
        DisplayBrightnessTextBox.Text = settings.Device.DisplayBrightness.ToString("0.00", CultureInfo.InvariantCulture);
        EncoderInvertCheckBox.IsChecked = settings.Device.EncoderInvert;
        SelectAudioMode(settings.AudioMode);
    }

    private void ApplyDeviceStatus(DeviceStatusDto status)
    {
        DeviceStatusText.Text = status.IsConnected
            ? $"Connected ({status.PortName ?? "unknown"})"
            : "Disconnected";
    }

    private void ApplyTarget(ActiveTargetDto target)
    {
        TargetStatusText.Text = target.Kind == TargetKinds.SessionById
            ? $"Target: {target.SessionId}"
            : "Target: Master";
    }

    private void ApplyGraph(AudioGraphDto graph)
    {
        ApplyMaster(graph.Master);
        ReconcileSessions(graph.Sessions);
    }

    private void ApplyMaster(MasterAudioDto master)
    {
        _masterMuted = master.Muted;
        _lastMasterVolume = master.Volume;

        _suppressVolumeValueChanged = true;
        try
        {
            MasterVolumeSlider.Value = ScalarToPercent(master.Volume);
        }
        finally
        {
            _suppressVolumeValueChanged = false;
        }
    }

    private void ReconcileSessions(IReadOnlyList<AudioSessionDto> sessions)
    {
        var expectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            expectedIds.Add(session.SessionId);

            var item = UpsertSessionItem(session);
            var currentIndex = _sessionItems.IndexOf(item);
            if (currentIndex >= 0 && currentIndex != index)
            {
                _sessionItems.Move(currentIndex, index);
            }
        }

        for (var index = _sessionItems.Count - 1; index >= 0; index--)
        {
            var item = _sessionItems[index];
            if (expectedIds.Contains(item.SessionId))
            {
                continue;
            }

            _sessionItems.RemoveAt(index);
            _sessionItemsById.Remove(item.SessionId);
        }

        _lastSessions = sessions.ToList();
    }

    private void UpsertSession(AudioSessionDto session)
    {
        UpsertSessionItem(session);

        var list = _lastSessions.ToList();
        var index = list.FindIndex(x => string.Equals(x.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            list[index] = session;
        }
        else
        {
            list.Add(session);
        }

        _lastSessions = list;
    }

    private AudioSessionItem UpsertSessionItem(AudioSessionDto session)
    {
        if (!_sessionItemsById.TryGetValue(session.SessionId, out var item))
        {
            item = new AudioSessionItem(
                session.SessionId,
                string.IsNullOrWhiteSpace(session.DisplayName) ? session.SessionId : session.DisplayName,
                ScalarToPercent(session.Volume),
                session.Muted);

            _sessionItemsById[session.SessionId] = item;
            _sessionItems.Add(item);
            return item;
        }

        _suppressVolumeValueChanged = true;
        try
        {
            item.Display = string.IsNullOrWhiteSpace(session.DisplayName) ? session.SessionId : session.DisplayName;
            item.VolumePercent = ScalarToPercent(session.Volume);
            item.Muted = session.Muted;
        }
        finally
        {
            _suppressVolumeValueChanged = false;
        }

        return item;
    }

    private void RemoveSession(string sessionId)
    {
        if (_sessionItemsById.Remove(sessionId, out var item))
        {
            _sessionItems.Remove(item);
        }

        _lastSessions = _lastSessions
            .Where(x => !string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void SelectAudioMode(AudioMode mode)
    {
        foreach (var item in AudioModeComboBox.Items)
        {
            if (item is ComboBoxItem comboItem && string.Equals(comboItem.Content?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                AudioModeComboBox.SelectedItem = comboItem;
                return;
            }
        }

        AudioModeComboBox.SelectedIndex = 0;
    }

    private AudioMode ReadAudioModeSelection()
    {
        if (AudioModeComboBox.SelectedItem is ComboBoxItem comboItem && Enum.TryParse<AudioMode>(comboItem.Content?.ToString(), ignoreCase: true, out var mode))
        {
            return mode;
        }

        return AudioMode.Real;
    }

    private static float PercentToScalar(double percent)
    {
        return (float)Math.Clamp(percent / 100d, 0d, 1d);
    }

    private static double ScalarToPercent(float value)
    {
        return Math.Clamp(value * 100d, 0d, 100d);
    }

    private static bool NearlyEqual(float a, float b)
    {
        return Math.Abs(a - b) < 0.005f;
    }

    private static bool TryGetSessionItem(object sender, [NotNullWhen(true)] out AudioSessionItem? item)
    {
        item = (sender as FrameworkElement)?.Tag as AudioSessionItem;
        return item is not null;
    }

    private bool TryGetKnownSessionVolume(string sessionId, out float volume)
    {
        var known = _lastSessions.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        if (known is null)
        {
            volume = 0f;
            return false;
        }

        volume = known.Volume;
        return true;
    }

    private void UpdateKnownSessionVolume(string sessionId, float volume)
    {
        var list = _lastSessions.ToList();
        var index = list.FindIndex(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        list[index] = list[index] with { Volume = volume };
        _lastSessions = list;
    }

    private void ScheduleMasterVolumeSend(bool immediate)
    {
        if (_suppressVolumeValueChanged)
        {
            return;
        }

        var debounceCts = ReplaceMasterVolumeDebounceToken();
        var percent = MasterVolumeSlider.Value;

        _ = SendMasterVolumeAsync(percent, debounceCts, immediate);
    }

    private CancellationTokenSource ReplaceMasterVolumeDebounceToken()
    {
        lock (_volumeDebounceSync)
        {
            _masterVolumeDebounceCts?.Cancel();
            _masterVolumeDebounceCts?.Dispose();

            _masterVolumeDebounceCts = new CancellationTokenSource();
            return _masterVolumeDebounceCts;
        }
    }

    private void ScheduleSessionVolumeSend(AudioSessionItem item, bool immediate)
    {
        if (_suppressVolumeValueChanged)
        {
            return;
        }

        var debounceCts = ReplaceSessionVolumeDebounceToken(item.SessionId);
        var percent = item.VolumePercent;

        _ = SendSessionVolumeAsync(item.SessionId, percent, debounceCts, immediate);
    }

    private CancellationTokenSource ReplaceSessionVolumeDebounceToken(string sessionId)
    {
        lock (_volumeDebounceSync)
        {
            if (_sessionVolumeDebounce.TryGetValue(sessionId, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            var next = new CancellationTokenSource();
            _sessionVolumeDebounce[sessionId] = next;
            return next;
        }
    }

    private async Task SendMasterVolumeAsync(double percent, CancellationTokenSource debounceCts, bool immediate)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(RealtimeVolumeSendDebounce, debounceCts.Token);
            }

            var requested = PercentToScalar(percent);
            if (NearlyEqual(requested, _lastMasterVolume))
            {
                return;
            }

            await _backendClient.SendCommandAsync<SetMasterVolumeRequest, AckResponse>(
                CommandNames.AudioSetMasterVolume,
                new SetMasterVolumeRequest(requested),
                debounceCts.Token);

            _lastMasterVolume = requested;
        }
        catch (OperationCanceledException)
        {
            // Expected for debounced realtime updates.
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _suppressVolumeValueChanged = true;
                try
                {
                    MasterVolumeSlider.Value = ScalarToPercent(_lastMasterVolume);
                }
                finally
                {
                    _suppressVolumeValueChanged = false;
                }

                StatusText.Text = $"Master volume failed: {ex.Message}";
            });
        }
        finally
        {
            lock (_volumeDebounceSync)
            {
                if (ReferenceEquals(_masterVolumeDebounceCts, debounceCts))
                {
                    _masterVolumeDebounceCts = null;
                }
            }

            debounceCts.Dispose();
        }
    }

    private async Task SendSessionVolumeAsync(string sessionId, double percent, CancellationTokenSource debounceCts, bool immediate)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(RealtimeVolumeSendDebounce, debounceCts.Token);
            }

            var requested = PercentToScalar(percent);
            var known = _lastSessions.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
            if (known is not null && NearlyEqual(requested, known.Volume))
            {
                return;
            }

            await _backendClient.SendCommandAsync<SetSessionVolumeRequest, AckResponse>(
                CommandNames.AudioSetSessionVolume,
                new SetSessionVolumeRequest(sessionId, requested),
                debounceCts.Token);

            UpdateKnownSessionVolume(sessionId, requested);
        }
        catch (OperationCanceledException)
        {
            // Expected for debounced realtime updates.
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_sessionItemsById.TryGetValue(sessionId, out var item) && TryGetKnownSessionVolume(sessionId, out var knownVolume))
                {
                    _suppressVolumeValueChanged = true;
                    try
                    {
                        item.VolumePercent = ScalarToPercent(knownVolume);
                    }
                    finally
                    {
                        _suppressVolumeValueChanged = false;
                    }
                }

                StatusText.Text = $"Session volume failed: {ex.Message}";
            });
        }
        finally
        {
            lock (_volumeDebounceSync)
            {
                if (_sessionVolumeDebounce.TryGetValue(sessionId, out var current) && ReferenceEquals(current, debounceCts))
                {
                    _sessionVolumeDebounce.Remove(sessionId);
                }
            }

            debounceCts.Dispose();
        }
    }

    private void CancelPendingVolumeSends()
    {
        lock (_volumeDebounceSync)
        {
            _masterVolumeDebounceCts?.Cancel();
            _masterVolumeDebounceCts?.Dispose();
            _masterVolumeDebounceCts = null;

            foreach (var cts in _sessionVolumeDebounce.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _sessionVolumeDebounce.Clear();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var port = string.IsNullOrWhiteSpace(PortNameTextBox.Text) ? null : PortNameTextBox.Text.Trim();
            var response = await _backendClient.SendCommandAsync<ConnectDeviceRequest, DeviceStatusResponse>(
                CommandNames.DeviceConnect,
                new ConnectDeviceRequest(port));

            ApplyDeviceStatus(response.Status);
            StatusText.Text = "Device connect command sent.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Connect failed: {ex.Message}";
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var response = await _backendClient.SendCommandAsync<EmptyRequest, DeviceStatusResponse>(
                CommandNames.DeviceDisconnect,
                new EmptyRequest());

            ApplyDeviceStatus(response.Status);
            StatusText.Text = "Device disconnected.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Disconnect failed: {ex.Message}";
        }
    }

    private async void ConnectSimulatorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PortNameTextBox.Text = "sim";
            var response = await _backendClient.SendCommandAsync<ConnectDeviceRequest, DeviceStatusResponse>(
                CommandNames.DeviceConnect,
                new ConnectDeviceRequest("sim"));

            ApplyDeviceStatus(response.Status);
            StatusText.Text = "Simulator connected.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Simulator connect failed: {ex.Message}";
        }
    }

    private async void SelectMasterTargetButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var response = await _backendClient.SendCommandAsync<SelectTargetRequest, TargetResponse>(
                CommandNames.TargetSelect,
                new SelectTargetRequest(new ActiveTargetDto(TargetKinds.Master, null, null)));

            ApplyTarget(response.Target);
            StatusText.Text = "Target set to master.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Target update failed: {ex.Message}";
        }
    }

    private async void SelectSessionTargetFromRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSessionItem(sender, out var item))
        {
            StatusText.Text = "Session command failed: row context missing.";
            return;
        }

        try
        {
            var response = await _backendClient.SendCommandAsync<SelectTargetRequest, TargetResponse>(
                CommandNames.TargetSelect,
                new SelectTargetRequest(new ActiveTargetDto(TargetKinds.SessionById, item.SessionId, null)));

            ApplyTarget(response.Target);
            StatusText.Text = $"Target set to {item.Display}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Target update failed: {ex.Message}";
        }
    }

    private void MasterVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ScheduleMasterVolumeSend(immediate: false);
    }

    private void MasterVolumeSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        ScheduleMasterVolumeSend(immediate: true);
    }

    private void SessionVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!TryGetSessionItem(sender, out var item))
        {
            return;
        }

        ScheduleSessionVolumeSend(item, immediate: false);
    }

    private void SessionVolumeSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!TryGetSessionItem(sender, out var item))
        {
            return;
        }

        ScheduleSessionVolumeSend(item, immediate: true);
    }

    private async void ToggleMasterMuteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var requestedMute = !_masterMuted;
            await _backendClient.SendCommandAsync<SetMasterMuteRequest, AckResponse>(
                CommandNames.AudioSetMasterMute,
                new SetMasterMuteRequest(requestedMute));

            _masterMuted = requestedMute;
            StatusText.Text = requestedMute ? "Master muted." : "Master unmuted.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Master mute failed: {ex.Message}";
        }
    }

    private async void ToggleSessionMuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSessionItem(sender, out var item))
        {
            StatusText.Text = "Session command failed: row context missing.";
            return;
        }

        var match = _lastSessions.FirstOrDefault(s => string.Equals(s.SessionId, item.SessionId, StringComparison.OrdinalIgnoreCase));
        var currentMute = match?.Muted ?? item.Muted;

        try
        {
            var requestedMute = !currentMute;
            await _backendClient.SendCommandAsync<SetSessionMuteRequest, AckResponse>(
                CommandNames.AudioSetSessionMute,
                new SetSessionMuteRequest(item.SessionId, requestedMute));

            item.Muted = requestedMute;
            StatusText.Text = requestedMute ? $"{item.Display} muted." : $"{item.Display} unmuted.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Session mute failed: {ex.Message}";
        }
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var deviceSettings = new DeviceSettingsDto(
                int.Parse(DetentCountTextBox.Text, CultureInfo.InvariantCulture),
                float.Parse(DetentStrengthTextBox.Text, CultureInfo.InvariantCulture),
                0.40f,
                float.Parse(LedBrightnessTextBox.Text, CultureInfo.InvariantCulture),
                float.Parse(DisplayBrightnessTextBox.Text, CultureInfo.InvariantCulture),
                EncoderInvertCheckBox.IsChecked == true,
                450);

            var appSettings = new AppSettingsDto(deviceSettings, ReadAudioModeSelection());

            await _backendClient.SendCommandAsync<UpdateSettingsRequest, SettingsResponse>(
                CommandNames.SettingsUpdate,
                new UpdateSettingsRequest(appSettings));

            await RefreshSettingsAsync();
            StatusText.Text = "Settings saved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save settings failed: {ex.Message}";
        }
    }
}



