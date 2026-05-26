using System.IO.Ports;
using AuraWave.Core.Enums;
using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;

namespace AuraWave.Hardware.RfSwitch;

/// <summary>ASCII RF switch: SET:E|H|V, GET?, OK:path</summary>
public sealed class SerialRfSwitchController : IRfSwitchController
{
    private readonly ILogService _log;
    private SerialPort? _port;
    private readonly RfSwitchStatus _status = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _status.IsConnected;
    public RfSwitchStatus Status => _status;

    public SerialRfSwitchController(ILogService log) => _log = log;

    public async Task<bool> ConnectAsync(string portName, CancellationToken ct = default)
    {
        try
        {
            await Task.Run(() =>
            {
                _port = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 2000,
                    WriteTimeout = 2000,
                    NewLine = "\r\n"
                };
                _port.Open();
            }, ct);

            _status.IsConnected = true;
            _status.PortName = portName;
            _log.Info("RF", $"Connected on {portName}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("RF", ex.Message);
            return false;
        }
    }

    public Task DisconnectAsync()
    {
        if (_port?.IsOpen == true) _port.Close();
        _status.IsConnected = false;
        return Task.CompletedTask;
    }

    public async Task SetPathAsync(PolarizationType polarization, CancellationToken ct = default)
    {
        string cmd = polarization switch
        {
            PolarizationType.EPlane => "SET:E",
            PolarizationType.HPlane => "SET:H",
            _ => "SET:V"
        };
        await SendAsync(cmd, ct);
        _status.ActivePath = polarization;
        _status.LastUpdate = DateTime.UtcNow;
    }

    public async Task<PolarizationType> GetActivePathAsync(CancellationToken ct = default)
    {
        var resp = await SendAsync("GET?", ct);
        if (resp.Contains('E', StringComparison.OrdinalIgnoreCase)) return PolarizationType.EPlane;
        if (resp.Contains('H', StringComparison.OrdinalIgnoreCase)) return PolarizationType.HPlane;
        return _status.ActivePath;
    }

    private async Task<string> SendAsync(string cmd, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _log.Scpi("RF>>", cmd);
            await Task.Run(() => _port!.WriteLine(cmd), ct);
            var line = await Task.Run(() => _port!.ReadLine(), ct);
            _log.Scpi("RF<<", line);
            return line;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
        _port?.Dispose();
    }
}
