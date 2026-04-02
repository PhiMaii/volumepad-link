using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Core;
using VolumePadLink.Agent.Options;
using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services.Tray;

public sealed class TrayService(
    IHostApplicationLifetime hostApplicationLifetime,
    IDeviceService deviceService,
    RuntimeStateStore stateStore,
    IOptions<AgentRuntimeOptions> runtimeOptions,
    ILogger<TrayService> logger) : IHostedService, IDisposable
{
    private readonly object _gate = new();
    private Thread? _trayThread;
    private SynchronizationContext? _traySynchronizationContext;
    private NotifyIcon? _notifyIcon;
    private ManualResetEventSlim? _readySignal;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            logger.LogInformation("Tray integration is only available on Windows.");
            return Task.CompletedTask;
        }

        _readySignal = new ManualResetEventSlim(false);
        _trayThread = new Thread(TrayThreadMain)
        {
            IsBackground = true,
            Name = "VolumePadLink.Tray",
        };
        _trayThread.SetApartmentState(ApartmentState.STA);
        _trayThread.Start();
        _readySignal.Wait(cancellationToken);

        logger.LogInformation("Tray integration started.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var context = _traySynchronizationContext;
        if (context is null)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(_ =>
        {
            try
            {
                Application.ExitThread();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, null);

        return completion.Task;
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
        _readySignal?.Dispose();
    }

    private void TrayThreadMain()
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _traySynchronizationContext = SynchronizationContext.Current;

        using var menu = new ContextMenuStrip();
        menu.Items.Add("Open", image: null, OnOpenUiClicked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Connect", image: null, OnConnectClicked);
        menu.Items.Add("Reconnect", image: null, OnReconnectClicked);
        menu.Items.Add("Disconnect", image: null, OnDisconnectClicked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", image: null, (_, _) => hostApplicationLifetime.StopApplication());

        _notifyIcon = new NotifyIcon
        {
            Text = "VolumePad Link Agent",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };

        using var updateTimer = new System.Windows.Forms.Timer { Interval = 500 };
        updateTimer.Tick += (_, _) => UpdateTrayIconFromState();
        updateTimer.Start();
        UpdateTrayIconFromState();

        _readySignal?.Set();
        Application.Run();

        updateTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }

    private void UpdateTrayIconFromState()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var status = stateStore.GetSnapshot().DeviceStatus;
        var (icon, label) = status.ConnectionState switch
        {
            ConnectionState.Connected => (SystemIcons.Information, "Connected"),
            ConnectionState.Connecting => (SystemIcons.Warning, "Connecting"),
            ConnectionState.Reconnecting => (SystemIcons.Warning, "Reconnecting"),
            ConnectionState.Error => (SystemIcons.Error, "Error"),
            _ => (SystemIcons.Application, "Disconnected"),
        };

        _notifyIcon.Icon = icon;
        var port = string.IsNullOrWhiteSpace(status.PortName) ? "no port" : status.PortName;
        _notifyIcon.Text = $"VolumePad Link ({label}, {port})";
    }

    private void OnOpenUiClicked(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "VolumePadLink.UI.exe",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Unable to launch VolumePadLink.UI.exe from tray.");
        }
    }

    private void OnConnectClicked(object? sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await deviceService.ConnectAsync(runtimeOptions.Value.DefaultAutoConnectPort, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Tray connect action failed.");
            }
        });
    }

    private void OnReconnectClicked(object? sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await deviceService.ReconnectAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Tray reconnect action failed.");
            }
        });
    }

    private void OnDisconnectClicked(object? sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await deviceService.DisconnectAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Tray disconnect action failed.");
            }
        });
    }
}
