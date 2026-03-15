using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VolumePadLink.Contracts.Abstractions;
using VolumePadLink.Contracts.Commands;
using VolumePadLink.Contracts.DTOs;
using VolumePadLink.UI.Models;
using VolumePadLink.UI.Services;

namespace VolumePadLink.UI;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan EventRefreshDebounce = TimeSpan.FromMilliseconds(300);
    private static readonly HashSet<string> RefreshEventNames = new(StringComparer.Ordinal)
    {
        EventNames.AudioGraphChanged,
        EventNames.AudioMasterChanged,
        EventNames.AudioSessionAdded,
        EventNames.AudioSessionUpdated,
        EventNames.AudioSessionRemoved,
        EventNames.DeviceConnected,
        EventNames.DeviceDisconnected,
        EventNames.DeviceCapabilitiesReceived,
        EventNames.DeviceSettingsApplied,
        EventNames.TargetActiveChanged
    };

    private readonly BackendClient _backendClient = new();
    private readonly CancellationTokenSource _eventLoopCts = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private Task? _eventLoopTask;
    private bool _masterMuted;
    private IReadOnlyList<AudioSessionDto> _lastSessions = [];
    private DateTimeOffset _lastEventDrivenRefreshUtc = DateTimeOffset.MinValue;
    private int _eventRefreshScheduled;

    public MainWindow()
    {
        InitializeComponent();

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
        if (!RefreshEventNames.Contains(message.Name))
        {
            return Task.CompletedTask;
        }

        if (Interlocked.Exchange(ref _eventRefreshScheduled, 1) == 1)
        {
            return Task.CompletedTask;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = RefreshFromEventAsync());
        return Task.CompletedTask;
    }

    private async Task RefreshFromEventAsync()
    {
        try
        {
            await _refreshGate.WaitAsync();
            try
            {
                var elapsed = DateTimeOffset.UtcNow - _lastEventDrivenRefreshUtc;
                if (elapsed < EventRefreshDebounce)
                {
                    await Task.Delay(EventRefreshDebounce - elapsed);
                }

                await RefreshAsync();
                _lastEventDrivenRefreshUtc = DateTimeOffset.UtcNow;
            }
            finally
            {
                _refreshGate.Release();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _eventRefreshScheduled, 0);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var ping = await _backendClient.SendCommandAsync<EmptyRequest, PingResponse>(CommandNames.AppPing, new EmptyRequest());
            AgentStatusText.Text = $"Connected. Agent {ping.Version} at {ping.UtcNow:HH:mm:ss} UTC";

            var deviceStatus = await _backendClient.SendCommandAsync<EmptyRequest, DeviceStatusResponse>(CommandNames.DeviceGetStatus, new EmptyRequest());
            DeviceStatusText.Text = deviceStatus.Status.IsConnected
                ? $"Connected ({deviceStatus.Status.PortName ?? "unknown"})"
                : "Disconnected";

            var target = await _backendClient.SendCommandAsync<EmptyRequest, TargetResponse>(CommandNames.TargetGetActive, new EmptyRequest());
            TargetStatusText.Text = target.Target.Kind == TargetKinds.SessionById
                ? $"Target: {target.Target.SessionId}"
                : "Target: Master";

            var graphResponse = await _backendClient.SendCommandAsync<EmptyRequest, AudioGraphResponse>(CommandNames.AudioGetGraph, new EmptyRequest());
            var graph = graphResponse.Graph;
            _masterMuted = graph.Master.Muted;
            MasterVolumeSlider.Value = graph.Master.Volume;

            _lastSessions = graph.Sessions;
            SessionsListView.ItemsSource = graph.Sessions
                .Select(session => new AudioSessionItem(session.SessionId, session.DisplayName, session.Volume, session.Muted))
                .ToList();

            var settingsResponse = await _backendClient.SendCommandAsync<EmptyRequest, SettingsResponse>(CommandNames.SettingsGet, new EmptyRequest());
            var settings = settingsResponse.Settings;

            DetentCountTextBox.Text = settings.Device.DetentCount.ToString(CultureInfo.InvariantCulture);
            DetentStrengthTextBox.Text = settings.Device.DetentStrength.ToString("0.00", CultureInfo.InvariantCulture);
            LedBrightnessTextBox.Text = settings.Device.LedBrightness.ToString("0.00", CultureInfo.InvariantCulture);
            DisplayBrightnessTextBox.Text = settings.Device.DisplayBrightness.ToString("0.00", CultureInfo.InvariantCulture);
            EncoderInvertCheckBox.IsChecked = settings.Device.EncoderInvert;
            SelectAudioMode(settings.AudioMode);

            StatusText.Text = "Refreshed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Refresh failed: {ex.Message}";
        }
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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var port = string.IsNullOrWhiteSpace(PortNameTextBox.Text) ? null : PortNameTextBox.Text.Trim();
            await _backendClient.SendCommandAsync<ConnectDeviceRequest, DeviceStatusResponse>(CommandNames.DeviceConnect, new ConnectDeviceRequest(port));
            await RefreshAsync();
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
            await _backendClient.SendCommandAsync<EmptyRequest, DeviceStatusResponse>(CommandNames.DeviceDisconnect, new EmptyRequest());
            await RefreshAsync();
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
            await _backendClient.SendCommandAsync<ConnectDeviceRequest, DeviceStatusResponse>(CommandNames.DeviceConnect, new ConnectDeviceRequest("sim"));
            await RefreshAsync();
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
            await _backendClient.SendCommandAsync<SelectTargetRequest, TargetResponse>(
                CommandNames.TargetSelect,
                new SelectTargetRequest(new ActiveTargetDto(TargetKinds.Master, null, null)));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Target update failed: {ex.Message}";
        }
    }

    private async void SelectSessionTargetButton_Click(object sender, RoutedEventArgs e)
    {
        if (SessionsListView.SelectedItem is not AudioSessionItem selected)
        {
            StatusText.Text = "Select a session first.";
            return;
        }

        try
        {
            await _backendClient.SendCommandAsync<SelectTargetRequest, TargetResponse>(
                CommandNames.TargetSelect,
                new SelectTargetRequest(new ActiveTargetDto(TargetKinds.SessionById, selected.SessionId, null)));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Target update failed: {ex.Message}";
        }
    }

    private async void ApplyMasterVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _backendClient.SendCommandAsync<SetMasterVolumeRequest, AckResponse>(
                CommandNames.AudioSetMasterVolume,
                new SetMasterVolumeRequest((float)MasterVolumeSlider.Value));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Master volume failed: {ex.Message}";
        }
    }

    private async void ToggleMasterMuteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _backendClient.SendCommandAsync<SetMasterMuteRequest, AckResponse>(
                CommandNames.AudioSetMasterMute,
                new SetMasterMuteRequest(!_masterMuted));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Master mute failed: {ex.Message}";
        }
    }

    private async void ToggleSelectedSessionMuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SessionsListView.SelectedItem is not AudioSessionItem selected)
        {
            StatusText.Text = "Select a session first.";
            return;
        }

        var match = _lastSessions.FirstOrDefault(s => string.Equals(s.SessionId, selected.SessionId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            StatusText.Text = "Session no longer available.";
            return;
        }

        try
        {
            await _backendClient.SendCommandAsync<SetSessionMuteRequest, AckResponse>(
                CommandNames.AudioSetSessionMute,
                new SetSessionMuteRequest(match.SessionId, !match.Muted));
            await RefreshAsync();
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

            StatusText.Text = "Settings saved.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save settings failed: {ex.Message}";
        }
    }
}
