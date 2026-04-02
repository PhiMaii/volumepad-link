using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;
using VolumePadLink.UI.Services;

namespace VolumePadLink.UI;

public sealed partial class MainWindow : Window
{
    private const string AgentPipeName = "VolumePadLink.Agent.v2";

    private readonly AgentIpcClient _client = new();
    private readonly JsonSerializerOptions _prettyJsonOptions = new(ProtocolJson.SerializerOptions)
    {
        WriteIndented = true,
    };

    private readonly TextBlock _agentStatusText = new() { Text = "Disconnected" };
    private readonly TextBlock _deviceStatusText = new() { Text = "Disconnected" };
    private readonly ComboBox _portsComboBox = new() { Width = 220, PlaceholderText = "Select port" };
    private readonly Slider _volumeSlider = new() { Width = 280, Minimum = 0, Maximum = 1, StepFrequency = 0.01 };
    private readonly ProgressBar _peakLevelBar = new() { Width = 320, Minimum = 0, Maximum = 1, Height = 16 };
    private readonly ProgressBar _rmsLevelBar = new() { Width = 320, Minimum = 0, Maximum = 1, Height = 16 };
    private readonly TextBlock _peakLevelText = new() { Text = "0%" };
    private readonly TextBlock _rmsLevelText = new() { Text = "0%" };
    private readonly TextBlock _meterInfoText = new() { Text = "Mode: - | Status: -" };
    private readonly TextBox _masterStateTextBox = CreateReadOnlyTextBox(150);
    private readonly TextBox _debugStateTextBox = CreateReadOnlyTextBox(150);
    private readonly TextBox _diagnosticsTextBox = CreateReadOnlyTextBox(220);

    private readonly CheckBox _autoReconnectOnErrorCheckBox = new();
    private readonly CheckBox _autoConnectOnStartupCheckBox = new();
    private readonly NumberBox _volumeStepSizeNumberBox = CreateNumberBox(0.001, 0.20, 0.02);
    private readonly NumberBox _detentCountNumberBox = CreateNumberBox(0, 128, 24);
    private readonly NumberBox _detentStrengthNumberBox = CreateNumberBox(0.0, 1.0, 0.65);
    private readonly NumberBox _snapStrengthNumberBox = CreateNumberBox(0.0, 1.0, 0.40);
    private readonly CheckBox _encoderInvertCheckBox = new();
    private readonly NumberBox _ledBrightnessNumberBox = CreateNumberBox(0.0, 1.0, 0.80);
    private readonly ComboBox _meterModeComboBox = new();
    private readonly TextBox _meterColorTextBox = new() { Width = 160 };
    private readonly NumberBox _meterBrightnessNumberBox = CreateNumberBox(0.0, 1.0, 0.80);
    private readonly NumberBox _meterGainNumberBox = CreateNumberBox(0.10, 8.0, 1.0);
    private readonly NumberBox _meterSmoothingNumberBox = CreateNumberBox(0.0, 1.0, 0.25);
    private readonly NumberBox _meterPeakHoldMsNumberBox = CreateNumberBox(0, 3000, 500);
    private readonly NumberBox _meterMuteRedDurationMsNumberBox = CreateNumberBox(50, 3000, 700);
    private readonly CheckBox _lowEndstopEnabledCheckBox = new();
    private readonly NumberBox _lowEndstopPositionNumberBox = CreateNumberBox(-1.0, 1.0, -1.0);
    private readonly NumberBox _lowEndstopStrengthNumberBox = CreateNumberBox(0.0, 1.0, 0.70);
    private readonly CheckBox _highEndstopEnabledCheckBox = new();
    private readonly NumberBox _highEndstopPositionNumberBox = CreateNumberBox(-1.0, 1.0, 1.0);
    private readonly NumberBox _highEndstopStrengthNumberBox = CreateNumberBox(0.0, 1.0, 0.70);

    private readonly NumberBox _debugDetentStrengthMaxVPerRadNumberBox = CreateNumberBox(0.0, 20.0, 2.0);
    private readonly NumberBox _debugSnapStrengthMaxVPerRadNumberBox = CreateNumberBox(0.0, 20.0, 2.0);
    private readonly NumberBox _debugClickPulseVoltageNumberBox = CreateNumberBox(0.0, 10.0, 1.2);
    private readonly NumberBox _debugClickPulseMsNumberBox = CreateNumberBox(0, 2000, 34);
    private readonly NumberBox _debugEndstopMinPosNumberBox = CreateNumberBox(-1.0, 1.0, -1.0);
    private readonly NumberBox _debugEndstopMaxPosNumberBox = CreateNumberBox(-1.0, 1.0, 1.0);
    private readonly NumberBox _debugEndstopMinStrengthNumberBox = CreateNumberBox(0.0, 1.0, 0.7);
    private readonly NumberBox _debugEndstopMaxStrengthNumberBox = CreateNumberBox(0.0, 1.0, 0.7);
    private readonly NumberBox _debugStreamIntervalNumberBox = CreateNumberBox(30, 3000, 150);

    private bool _meterRenderInitialized;
    private double _meterRenderedPeak;
    private double _meterRenderedRms;
    private double _meterHeldPeak;
    private DateTimeOffset _meterPeakHoldUntilUtc;

    public MainWindow()
    {
        InitializeComponent();
        BuildUi();

        _client.EventReceived += OnAgentEventReceived;
        _client.ConnectionLost += OnConnectionLost;

        _masterStateTextBox.Text = "No data";
        _debugStateTextBox.Text = "No data";
        _diagnosticsTextBox.Text = "No diagnostics yet.";
        ApplySettingsToControls(new AppSettings());
        ApplyDebugTuningToControls(new DebugTuning());

        Closed += OnClosed;
    }

    private static TextBox CreateReadOnlyTextBox(double height)
    {
        var textBox = new TextBox
        {
            Height = height,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);
        return textBox;
    }

    private static NumberBox CreateNumberBox(double min, double max, double value)
    {
        return new NumberBox
        {
            Width = 160,
            Minimum = min,
            Maximum = max,
            Value = value,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
    }

    private void BuildUi()
    {
        _meterModeComboBox.Items.Add(MeterModes.RingFill);
        _meterModeComboBox.Items.Add(MeterModes.VuPeakHold);
        _meterModeComboBox.Items.Add(MeterModes.PeakIndicator);

        var scrollViewer = new ScrollViewer();
        var rootPanel = new StackPanel
        {
            Spacing = 14,
            Padding = new Thickness(12),
        };
        scrollViewer.Content = rootPanel;
        RootGrid.Children.Add(scrollViewer);

        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        rootPanel.Children.Add(headerPanel);
        headerPanel.Children.Add(CreateButton("Connect Agent", ConnectAgent_Click));
        headerPanel.Children.Add(new TextBlock { Text = "Agent:", VerticalAlignment = VerticalAlignment.Center });
        headerPanel.Children.Add(_agentStatusText);
        headerPanel.Children.Add(new TextBlock { Text = "Device:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) });
        headerPanel.Children.Add(_deviceStatusText);

        rootPanel.Children.Add(new TextBlock { Text = "Device", FontSize = 18 });
        var deviceActionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        rootPanel.Children.Add(deviceActionPanel);
        deviceActionPanel.Children.Add(_portsComboBox);
        deviceActionPanel.Children.Add(CreateButton("Refresh Ports", RefreshPorts_Click));
        deviceActionPanel.Children.Add(CreateButton("Connect", ConnectDevice_Click));
        deviceActionPanel.Children.Add(CreateButton("Disconnect", DisconnectDevice_Click));
        deviceActionPanel.Children.Add(CreateButton("Reconnect", ReconnectDevice_Click));

        var volumePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        rootPanel.Children.Add(volumePanel);
        volumePanel.Children.Add(new TextBlock { Text = "Master Volume", VerticalAlignment = VerticalAlignment.Center });
        volumePanel.Children.Add(_volumeSlider);
        volumePanel.Children.Add(CreateButton("Apply Volume", ApplyVolume_Click));
        volumePanel.Children.Add(CreateButton("Toggle Mute", ToggleMute_Click));

        _masterStateTextBox.Header = "Master State";
        rootPanel.Children.Add(_masterStateTextBox);

        rootPanel.Children.Add(new TextBlock { Text = "Live Level", FontSize = 18, Margin = new Thickness(0, 8, 0, 0) });
        var liveLevelGrid = CreateTwoColumnFormGrid();
        AddFormRow(liveLevelGrid, "Peak", CreateProgressWithValue(_peakLevelBar, _peakLevelText));
        AddFormRow(liveLevelGrid, "RMS", CreateProgressWithValue(_rmsLevelBar, _rmsLevelText));
        AddFormRow(liveLevelGrid, "Mode / Status", _meterInfoText);
        rootPanel.Children.Add(liveLevelGrid);

        rootPanel.Children.Add(new TextBlock { Text = "Settings", FontSize = 18, Margin = new Thickness(0, 8, 0, 0) });
        var settingsActionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        rootPanel.Children.Add(settingsActionPanel);
        settingsActionPanel.Children.Add(CreateButton("Load Settings", LoadSettings_Click));
        settingsActionPanel.Children.Add(CreateButton("Apply Settings", ApplySettings_Click));
        settingsActionPanel.Children.Add(CreateButton("Apply Meter", ApplyMeterSettings_Click));

        var settingsGrid = CreateTwoColumnFormGrid();
        AddFormRow(settingsGrid, "Auto Reconnect On Error", _autoReconnectOnErrorCheckBox);
        AddFormRow(settingsGrid, "Auto Connect On Startup", _autoConnectOnStartupCheckBox);
        AddFormRow(settingsGrid, "Volume Step Size", _volumeStepSizeNumberBox);
        AddFormRow(settingsGrid, "Detent Count", _detentCountNumberBox);
        AddFormRow(settingsGrid, "Detent Strength", _detentStrengthNumberBox);
        AddFormRow(settingsGrid, "Snap Strength", _snapStrengthNumberBox);
        AddFormRow(settingsGrid, "Encoder Invert", _encoderInvertCheckBox);
        AddFormRow(settingsGrid, "LED Brightness", _ledBrightnessNumberBox);
        AddFormRow(settingsGrid, "Meter Mode", _meterModeComboBox);
        AddFormRow(settingsGrid, "Meter Color (#RRGGBB)", _meterColorTextBox);
        AddFormRow(settingsGrid, "Meter Brightness", _meterBrightnessNumberBox);
        AddFormRow(settingsGrid, "Meter Gain", _meterGainNumberBox);
        AddFormRow(settingsGrid, "Meter Smoothing", _meterSmoothingNumberBox);
        AddFormRow(settingsGrid, "Meter Peak Hold (ms)", _meterPeakHoldMsNumberBox);
        AddFormRow(settingsGrid, "Meter Mute Red Duration (ms)", _meterMuteRedDurationMsNumberBox);
        AddFormRow(settingsGrid, "Low Endstop Enabled", _lowEndstopEnabledCheckBox);
        AddFormRow(settingsGrid, "Low Endstop Position", _lowEndstopPositionNumberBox);
        AddFormRow(settingsGrid, "Low Endstop Strength", _lowEndstopStrengthNumberBox);
        AddFormRow(settingsGrid, "High Endstop Enabled", _highEndstopEnabledCheckBox);
        AddFormRow(settingsGrid, "High Endstop Position", _highEndstopPositionNumberBox);
        AddFormRow(settingsGrid, "High Endstop Strength", _highEndstopStrengthNumberBox);
        rootPanel.Children.Add(settingsGrid);

        rootPanel.Children.Add(new TextBlock { Text = "Debug", FontSize = 18, Margin = new Thickness(0, 8, 0, 0) });
        var debugActionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        rootPanel.Children.Add(debugActionPanel);
        debugActionPanel.Children.Add(CreateButton("Get State", GetDebugState_Click));
        debugActionPanel.Children.Add(CreateButton("Apply Tuning", ApplyDebugTuning_Click));
        debugActionPanel.Children.Add(CreateButton("Start Stream", StartDebugStream_Click));
        debugActionPanel.Children.Add(CreateButton("Stop Stream", StopDebugStream_Click));

        var debugGrid = CreateTwoColumnFormGrid();
        AddFormRow(debugGrid, "Detent Strength Max V/Rad", _debugDetentStrengthMaxVPerRadNumberBox);
        AddFormRow(debugGrid, "Snap Strength Max V/Rad", _debugSnapStrengthMaxVPerRadNumberBox);
        AddFormRow(debugGrid, "Click Pulse Voltage", _debugClickPulseVoltageNumberBox);
        AddFormRow(debugGrid, "Click Pulse (ms)", _debugClickPulseMsNumberBox);
        AddFormRow(debugGrid, "Endstop Min Pos", _debugEndstopMinPosNumberBox);
        AddFormRow(debugGrid, "Endstop Max Pos", _debugEndstopMaxPosNumberBox);
        AddFormRow(debugGrid, "Endstop Min Strength", _debugEndstopMinStrengthNumberBox);
        AddFormRow(debugGrid, "Endstop Max Strength", _debugEndstopMaxStrengthNumberBox);
        AddFormRow(debugGrid, "Debug Stream Interval (ms)", _debugStreamIntervalNumberBox);
        rootPanel.Children.Add(debugGrid);

        _debugStateTextBox.Header = "Debug State";
        rootPanel.Children.Add(_debugStateTextBox);

        rootPanel.Children.Add(new TextBlock { Text = "General", FontSize = 18, Margin = new Thickness(0, 8, 0, 0) });
        var restartButton = CreateButton("Restart Audio Backend", RestartAudioBackend_Click);
        restartButton.Width = 220;
        rootPanel.Children.Add(restartButton);
        _diagnosticsTextBox.Header = "Diagnostics";
        rootPanel.Children.Add(_diagnosticsTextBox);
    }

    private static Grid CreateTwoColumnFormGrid()
    {
        var grid = new Grid
        {
            ColumnSpacing = 10,
            RowSpacing = 8,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    private static void AddFormRow(Grid grid, string label, FrameworkElement control)
    {
        var rowIndex = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetRow(labelText, rowIndex);
        Grid.SetColumn(labelText, 0);
        Grid.SetRow(control, rowIndex);
        Grid.SetColumn(control, 1);

        grid.Children.Add(labelText);
        grid.Children.Add(control);
    }

    private static FrameworkElement CreateProgressWithValue(ProgressBar bar, TextBlock valueText)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                bar,
                valueText,
            },
        };
    }

    private static Button CreateButton(string content, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = content,
        };
        button.Click += handler;
        return button;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        await _client.DisposeAsync();
    }

    private async void ConnectAgent_Click(object sender, RoutedEventArgs e)
    {
        await EnsureAgentConnectionAsync();
        await RefreshPortsAsync();
        await RefreshDeviceStatusAsync();
        await RefreshMasterStateAsync();
        await LoadSettingsAsync();
        await LoadDebugStateAsync();
    }

    private async void RefreshPorts_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPortsAsync();
    }

    private async void ConnectDevice_Click(object sender, RoutedEventArgs e)
    {
        if (_portsComboBox.SelectedItem is not string portName)
        {
            AppendDiagnostics("Select a COM port first.");
            return;
        }

        try
        {
            await EnsureAgentConnectionAsync();
            var status = await _client.SendRequestAsync<DeviceStatus>(
                ProtocolNames.Methods.DeviceConnect,
                new DeviceConnectRequest(portName),
                CancellationToken.None);
            ApplyDeviceStatus(status);
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Connect failed: {ex.Message}");
        }
    }

    private async void DisconnectDevice_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var status = await _client.SendRequestAsync<DeviceStatus>(
                ProtocolNames.Methods.DeviceDisconnect,
                ProtocolJson.EmptyObject,
                CancellationToken.None);
            ApplyDeviceStatus(status);
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Disconnect failed: {ex.Message}");
        }
    }

    private async void ReconnectDevice_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var status = await _client.SendRequestAsync<DeviceStatus>(
                ProtocolNames.Methods.DeviceReconnect,
                ProtocolJson.EmptyObject,
                CancellationToken.None);
            ApplyDeviceStatus(status);
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Reconnect failed: {ex.Message}");
        }
    }

    private async void ApplyVolume_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var state = await _client.SendRequestAsync<AudioMasterState>(
                ProtocolNames.Methods.AudioMasterSetVolume,
                new SetVolumeRequest(_volumeSlider.Value),
                CancellationToken.None);
            ApplyAudioState(state);
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Set volume failed: {ex.Message}");
        }
    }

    private async void ToggleMute_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var state = await _client.SendRequestAsync<AudioMasterState>(
                ProtocolNames.Methods.AudioMasterToggleMute,
                ProtocolJson.EmptyObject,
                CancellationToken.None);
            ApplyAudioState(state);
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Toggle mute failed: {ex.Message}");
        }
    }

    private async void LoadSettings_Click(object sender, RoutedEventArgs e)
    {
        await LoadSettingsAsync();
    }

    private async void ApplySettings_Click(object sender, RoutedEventArgs e)
    {
        await ApplySettingsAsync("Settings applied.");
    }

    private async void ApplyMeterSettings_Click(object sender, RoutedEventArgs e)
    {
        await ApplySettingsAsync("Meter settings applied.");
    }

    private async void GetDebugState_Click(object sender, RoutedEventArgs e)
    {
        await LoadDebugStateAsync();
    }

    private async void ApplyDebugTuning_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var tuning = ReadDebugTuningFromControls();
            var state = await _client.SendRequestAsync<DebugState>(
                ProtocolNames.Methods.DebugApplyTuning,
                tuning,
                CancellationToken.None);

            _debugStateTextBox.Text = JsonSerializer.Serialize(state, _prettyJsonOptions);
            AppendDiagnostics("Debug tuning applied.");
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Apply debug tuning failed: {ex.Message}");
        }
    }

    private async void StartDebugStream_Click(object sender, RoutedEventArgs e)
    {
        await SetDebugStreamAsync(enabled: true);
    }

    private async void StopDebugStream_Click(object sender, RoutedEventArgs e)
    {
        await SetDebugStreamAsync(enabled: false);
    }

    private async void RestartAudioBackend_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureAgentConnectionAsync();
            await _client.SendRequestAsync<ServiceRestartAudioBackendResponse>(
                ProtocolNames.Methods.ServiceRestartAudioBackend,
                ProtocolJson.EmptyObject,
                CancellationToken.None);

            AppendDiagnostics("Audio backend restarted.");
            await RefreshMasterStateAsync();
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Restart audio backend failed: {ex.Message}");
        }
    }

    private async Task EnsureAgentConnectionAsync()
    {
        if (_client.IsConnected)
        {
            return;
        }

        _agentStatusText.Text = "Connecting...";
        await _client.ConnectAsync(AgentPipeName, CancellationToken.None);
        _agentStatusText.Text = "Connected";
    }

    private async Task RefreshPortsAsync()
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var response = await _client.SendRequestAsync<DeviceListPortsResponse>(
                ProtocolNames.Methods.DeviceListPorts,
                ProtocolJson.EmptyObject,
                CancellationToken.None);

            _portsComboBox.Items.Clear();
            foreach (var port in response.Ports)
            {
                _portsComboBox.Items.Add(port.PortName);
            }

            if (_portsComboBox.Items.Count > 0 && _portsComboBox.SelectedIndex < 0)
            {
                _portsComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Refresh ports failed: {ex.Message}");
        }
    }

    private async Task RefreshDeviceStatusAsync()
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var status = await _client.SendRequestAsync<DeviceStatus>(
                ProtocolNames.Methods.DeviceGetStatus,
                ProtocolJson.EmptyObject,
                CancellationToken.None);
            ApplyDeviceStatus(status);
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Get status failed: {ex.Message}");
        }
    }

    private async Task RefreshMasterStateAsync()
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var state = await _client.SendRequestAsync<AudioMasterState>(
                ProtocolNames.Methods.AudioMasterGet,
                ProtocolJson.EmptyObject,
                CancellationToken.None);
            ApplyAudioState(state);
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Get audio state failed: {ex.Message}");
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var response = await _client.SendRequestAsync<SettingsGetResponse>(
                ProtocolNames.Methods.SettingsGet,
                ProtocolJson.EmptyObject,
                CancellationToken.None);
            ApplySettingsToControls(response.Effective);
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Load settings failed: {ex.Message}");
        }
    }

    private async Task ApplySettingsAsync(string successMessage)
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var settings = ReadSettingsFromControls();
            var response = await _client.SendRequestAsync<SettingsUpdateResponse>(
                ProtocolNames.Methods.SettingsUpdate,
                settings,
                CancellationToken.None);

            ApplySettingsToControls(response.Effective);
            AppendDiagnostics(successMessage);
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Apply settings failed: {ex.Message}");
        }
    }

    private async Task LoadDebugStateAsync()
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var state = await _client.SendRequestAsync<DebugState>(
                ProtocolNames.Methods.DebugGetState,
                ProtocolJson.EmptyObject,
                CancellationToken.None);
            _debugStateTextBox.Text = JsonSerializer.Serialize(state, _prettyJsonOptions);
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Load debug state failed: {ex.Message}");
        }
    }

    private async Task SetDebugStreamAsync(bool enabled)
    {
        try
        {
            await EnsureAgentConnectionAsync();
            var interval = ToInt(_debugStreamIntervalNumberBox, 150);
            var state = await _client.SendRequestAsync<DebugState>(
                ProtocolNames.Methods.DebugSetStream,
                new DebugSetStreamRequest(enabled, interval),
                CancellationToken.None);
            _debugStateTextBox.Text = JsonSerializer.Serialize(state, _prettyJsonOptions);
            AppendDiagnostics(enabled ? "Debug stream started." : "Debug stream stopped.");
        }
        catch (Exception ex)
        {
            AppendDiagnostics($"Set debug stream failed: {ex.Message}");
        }
    }

    private AppSettings ReadSettingsFromControls()
    {
        return new AppSettings
        {
            AutoReconnectOnError = _autoReconnectOnErrorCheckBox.IsChecked == true,
            AutoConnectOnStartup = _autoConnectOnStartupCheckBox.IsChecked == true,
            VolumeStepSize = ToDouble(_volumeStepSizeNumberBox, 0.02),
            DetentCount = ToInt(_detentCountNumberBox, 24),
            DetentStrength = ToDouble(_detentStrengthNumberBox, 0.65),
            SnapStrength = ToDouble(_snapStrengthNumberBox, 0.40),
            EncoderInvert = _encoderInvertCheckBox.IsChecked == true,
            LedBrightness = ToDouble(_ledBrightnessNumberBox, 0.80),
            MeterMode = (_meterModeComboBox.SelectedItem as string) ?? MeterModes.RingFill,
            MeterColor = string.IsNullOrWhiteSpace(_meterColorTextBox.Text) ? "#00D26A" : _meterColorTextBox.Text.Trim(),
            MeterBrightness = ToDouble(_meterBrightnessNumberBox, 0.80),
            MeterGain = ToDouble(_meterGainNumberBox, 1.0),
            MeterSmoothing = ToDouble(_meterSmoothingNumberBox, 0.25),
            MeterPeakHoldMs = ToInt(_meterPeakHoldMsNumberBox, 500),
            MeterMuteRedDurationMs = ToInt(_meterMuteRedDurationMsNumberBox, 700),
            LowEndstopEnabled = _lowEndstopEnabledCheckBox.IsChecked == true,
            LowEndstopPosition = ToDouble(_lowEndstopPositionNumberBox, -1.0),
            LowEndstopStrength = ToDouble(_lowEndstopStrengthNumberBox, 0.70),
            HighEndstopEnabled = _highEndstopEnabledCheckBox.IsChecked == true,
            HighEndstopPosition = ToDouble(_highEndstopPositionNumberBox, 1.0),
            HighEndstopStrength = ToDouble(_highEndstopStrengthNumberBox, 0.70),
        };
    }

    private void ApplySettingsToControls(AppSettings settings)
    {
        _autoReconnectOnErrorCheckBox.IsChecked = settings.AutoReconnectOnError;
        _autoConnectOnStartupCheckBox.IsChecked = settings.AutoConnectOnStartup;
        _volumeStepSizeNumberBox.Value = settings.VolumeStepSize;
        _detentCountNumberBox.Value = settings.DetentCount;
        _detentStrengthNumberBox.Value = settings.DetentStrength;
        _snapStrengthNumberBox.Value = settings.SnapStrength;
        _encoderInvertCheckBox.IsChecked = settings.EncoderInvert;
        _ledBrightnessNumberBox.Value = settings.LedBrightness;
        _meterModeComboBox.SelectedItem = settings.MeterMode;
        _meterColorTextBox.Text = settings.MeterColor;
        _meterBrightnessNumberBox.Value = settings.MeterBrightness;
        _meterGainNumberBox.Value = settings.MeterGain;
        _meterSmoothingNumberBox.Value = settings.MeterSmoothing;
        _meterPeakHoldMsNumberBox.Value = settings.MeterPeakHoldMs;
        _meterMuteRedDurationMsNumberBox.Value = settings.MeterMuteRedDurationMs;
        _lowEndstopEnabledCheckBox.IsChecked = settings.LowEndstopEnabled;
        _lowEndstopPositionNumberBox.Value = settings.LowEndstopPosition;
        _lowEndstopStrengthNumberBox.Value = settings.LowEndstopStrength;
        _highEndstopEnabledCheckBox.IsChecked = settings.HighEndstopEnabled;
        _highEndstopPositionNumberBox.Value = settings.HighEndstopPosition;
        _highEndstopStrengthNumberBox.Value = settings.HighEndstopStrength;
    }

    private DebugTuning ReadDebugTuningFromControls()
    {
        return new DebugTuning
        {
            DetentStrengthMaxVPerRad = ToDouble(_debugDetentStrengthMaxVPerRadNumberBox, 2.0),
            SnapStrengthMaxVPerRad = ToDouble(_debugSnapStrengthMaxVPerRadNumberBox, 2.0),
            ClickPulseVoltage = ToDouble(_debugClickPulseVoltageNumberBox, 1.2),
            ClickPulseMs = ToInt(_debugClickPulseMsNumberBox, 34),
            EndstopMinPos = ToDouble(_debugEndstopMinPosNumberBox, -1.0),
            EndstopMaxPos = ToDouble(_debugEndstopMaxPosNumberBox, 1.0),
            EndstopMinStrength = ToDouble(_debugEndstopMinStrengthNumberBox, 0.7),
            EndstopMaxStrength = ToDouble(_debugEndstopMaxStrengthNumberBox, 0.7),
        };
    }

    private void ApplyDebugTuningToControls(DebugTuning tuning)
    {
        _debugDetentStrengthMaxVPerRadNumberBox.Value = tuning.DetentStrengthMaxVPerRad;
        _debugSnapStrengthMaxVPerRadNumberBox.Value = tuning.SnapStrengthMaxVPerRad;
        _debugClickPulseVoltageNumberBox.Value = tuning.ClickPulseVoltage;
        _debugClickPulseMsNumberBox.Value = tuning.ClickPulseMs;
        _debugEndstopMinPosNumberBox.Value = tuning.EndstopMinPos;
        _debugEndstopMaxPosNumberBox.Value = tuning.EndstopMaxPos;
        _debugEndstopMinStrengthNumberBox.Value = tuning.EndstopMinStrength;
        _debugEndstopMaxStrengthNumberBox.Value = tuning.EndstopMaxStrength;
    }

    private static double ToDouble(NumberBox box, double fallback)
    {
        return double.IsNaN(box.Value) ? fallback : box.Value;
    }

    private static int ToInt(NumberBox box, int fallback)
    {
        if (double.IsNaN(box.Value))
        {
            return fallback;
        }

        return Convert.ToInt32(Math.Round(box.Value, MidpointRounding.AwayFromZero));
    }

    private void ApplyDeviceStatus(DeviceStatus status)
    {
        _deviceStatusText.Text = $"{status.ConnectionState} ({status.PortName ?? "no-port"})";
    }

    private void ApplyAudioState(AudioMasterState state)
    {
        _volumeSlider.Value = state.Volume;
        _masterStateTextBox.Text = JsonSerializer.Serialize(state, _prettyJsonOptions);
    }

    private void ApplyMeterTick(MeterTick meterTick)
    {
        var inputPeak = Math.Clamp(EnsureFinite(meterTick.Peak), 0.0, 1.0);
        var inputRms = Math.Clamp(EnsureFinite(meterTick.Rms), 0.0, 1.0);
        var smoothing = Math.Clamp(EnsureFinite(meterTick.Smoothing), 0.0, 1.0);
        var interpolation = Math.Clamp(1.0 - smoothing, 0.02, 1.0);
        var peakHoldMs = Math.Max(0, meterTick.PeakHoldMs);
        var capturedAtUtc = meterTick.CapturedAtUtc == default ? DateTimeOffset.UtcNow : meterTick.CapturedAtUtc;

        if (!_meterRenderInitialized)
        {
            _meterRenderedPeak = inputPeak;
            _meterRenderedRms = inputRms;
            _meterHeldPeak = inputPeak;
            _meterPeakHoldUntilUtc = capturedAtUtc.AddMilliseconds(peakHoldMs);
            _meterRenderInitialized = true;
        }
        else
        {
            _meterRenderedPeak = SmoothToward(_meterRenderedPeak, inputPeak, interpolation);
            _meterRenderedRms = SmoothToward(_meterRenderedRms, inputRms, interpolation);

            if (_meterRenderedPeak >= _meterHeldPeak)
            {
                _meterHeldPeak = _meterRenderedPeak;
                _meterPeakHoldUntilUtc = capturedAtUtc.AddMilliseconds(peakHoldMs);
            }
            else if (peakHoldMs == 0 || capturedAtUtc >= _meterPeakHoldUntilUtc)
            {
                _meterHeldPeak = _meterRenderedPeak;
                _meterPeakHoldUntilUtc = capturedAtUtc.AddMilliseconds(peakHoldMs);
            }
        }

        var displayedPeak = Math.Clamp(_meterHeldPeak, 0.0, 1.0);
        var displayedRms = Math.Clamp(_meterRenderedRms, 0.0, 1.0);
        var gain = EnsureFinite(ToDouble(_meterGainNumberBox, 1.0), 1.0);

        _peakLevelBar.Value = displayedPeak;
        _rmsLevelBar.Value = displayedRms;
        _peakLevelText.Text = $"{displayedPeak:P0}";
        _rmsLevelText.Text = $"{displayedRms:P0}";
        _meterInfoText.Text = $"Mode: {meterTick.Mode} | Status: {(meterTick.Muted ? "Muted" : "Unmuted")} | Smooth: {smoothing:0.00} | Hold: {peakHoldMs}ms | Gain: {gain:0.00}x";
    }

    private static double SmoothToward(double current, double target, double interpolation)
    {
        return current + ((target - current) * interpolation);
    }

    private static double EnsureFinite(double value, double fallback = 0.0)
    {
        return double.IsFinite(value) ? value : fallback;
    }

    private void OnAgentEventReceived(object? sender, ProtocolEnvelope envelope)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                switch (envelope.Name)
                {
                    case ProtocolNames.Events.ConnectionStateChanged:
                        {
                            var evt = ProtocolJson.DeserializePayload<ConnectionStateChangedEvent>(envelope);
                            _deviceStatusText.Text = $"{evt.ConnectionState} ({evt.PortName ?? "no-port"})";
                            break;
                        }
                    case ProtocolNames.Events.AudioMasterChanged:
                        {
                            var state = ProtocolJson.DeserializePayload<AudioMasterState>(envelope);
                            ApplyAudioState(state);
                            break;
                        }
                    case ProtocolNames.Events.AudioMeterTick:
                        {
                            var tick = ProtocolJson.DeserializePayload<MeterTick>(envelope);
                            ApplyMeterTick(tick);
                            break;
                        }
                    case ProtocolNames.Events.SettingsApplied:
                        {
                            var evt = ProtocolJson.DeserializePayload<SettingsAppliedEvent>(envelope);
                            ApplySettingsToControls(evt.Effective);
                            break;
                        }
                    case ProtocolNames.Events.DebugState:
                        {
                            var evt = ProtocolJson.DeserializePayload<DebugStateEvent>(envelope);
                            _debugStateTextBox.Text = JsonSerializer.Serialize(evt.State, _prettyJsonOptions);
                            break;
                        }
                    case ProtocolNames.Events.Diagnostics:
                        {
                            var evt = ProtocolJson.DeserializePayload<DiagnosticsEvent>(envelope);
                            AppendDiagnostics($"[{evt.Level}] {evt.Code}: {evt.Message}");
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                AppendDiagnostics($"Failed handling event {envelope.Name}: {ex.Message}");
            }
        });
    }

    private void OnConnectionLost(object? sender, string reason)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _agentStatusText.Text = "Disconnected";
            AppendDiagnostics($"IPC disconnected: {reason}");
        });
    }

    private void AppendDiagnostics(string message)
    {
        var line = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        if (string.IsNullOrWhiteSpace(_diagnosticsTextBox.Text))
        {
            _diagnosticsTextBox.Text = line;
            return;
        }

        _diagnosticsTextBox.Text = $"{_diagnosticsTextBox.Text}{Environment.NewLine}{line}";
    }
}
