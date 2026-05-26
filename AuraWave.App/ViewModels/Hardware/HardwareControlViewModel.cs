using AuraWave.Core.Configuration;
using AuraWave.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO.Ports;

namespace AuraWave.App.ViewModels.Hardware;

public partial class HardwareControlViewModel : ObservableObject
{
    private readonly IHardwareManager _hw;
    private readonly ISafetyService _safety;
    private readonly HardwareConfiguration _config;
    private readonly ILogService _log;

    [ObservableProperty] private string _turntablePort = "COM3";
    [ObservableProperty] private int _turntableBaud = 115200;
    [ObservableProperty] private string _vnaAddress = string.Empty;
    [ObservableProperty] private string _rfSwitchPort = "COM4";
    [ObservableProperty] private double _targetAngle = 90;
    [ObservableProperty] private double _speedDegPerSec = 10;
    [ObservableProperty] private string _connectionSummary = "All instruments disconnected";
    [ObservableProperty] private double _liveAngle;
    [ObservableProperty] private bool _isEstopActive;

    public string[] AvailablePorts { get; private set; } = Array.Empty<string>();

    public HardwareControlViewModel(
        IHardwareManager hw,
        ISafetyService safety,
        HardwareConfiguration config,
        ILogService log)
    {
        _hw = hw;
        _safety = safety;
        _config = config;
        _log = log;

        TurntablePort = config.TurntablePort;
        TurntableBaud = config.TurntableBaud;
        VnaAddress = config.GetVnaConnectionString();
        RfSwitchPort = config.RfSwitchPort;
        SpeedDegPerSec = config.DefaultTurntableSpeedDegPerSec;

        RefreshPorts();
        _hw.Turntable.PositionChanged += (_, a) => LiveAngle = a;
        _hw.StateChanged += (_, s) => UpdateSummary(s);
        _safety.EmergencyStopActivated += (_, _) => IsEstopActive = true;
        _safety.EmergencyStopCleared += (_, _) => IsEstopActive = false;
    }

    private void UpdateSummary(Core.Models.SystemHardwareState s)
    {
        IsEstopActive = _safety.IsEmergencyStopActive || s.Turntable.EmergencyStop;
        var parts = new List<string>();
        if (s.Vna.IsConnected) parts.Add("VNA");
        if (s.Turntable.IsConnected)
            parts.Add(s.Turntable.EmergencyStop ? "TTL E-STOP" : s.Turntable.IsHomed ? "TTL calibrated" : "TTL (not calibrated)");
        if (s.RfSwitch.IsConnected) parts.Add("RF");
        ConnectionSummary = IsEstopActive ? "EMERGENCY STOP ACTIVE"
            : parts.Count > 0 ? string.Join(" · ", parts) : "Connect instruments below";
    }

    [RelayCommand]
    private void RefreshPorts()
    {
        AvailablePorts = SerialPort.GetPortNames();
        if (AvailablePorts.Length == 0)
            _log.Warning("HW", "No COM ports detected — plug in USB cables and refresh");
        else
            _log.Info("HW", $"COM ports: {string.Join(", ", AvailablePorts)}");
        OnPropertyChanged(nameof(AvailablePorts));
    }

    [RelayCommand]
    private async Task ConnectTurntableAsync()
    {
        _config.TurntablePort = TurntablePort;
        _config.TurntableBaud = TurntableBaud;
        _log.Info("TTL", $"Connecting turntable on {TurntablePort} @ {TurntableBaud}...");
        bool ok = await _hw.Turntable.ConnectAsync(TurntablePort, TurntableBaud);
        if (!ok)
            _log.Error("TTL", "Turntable connection failed — check port, wiring, and firmware");
        await _hw.RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task ConnectVnaAsync()
    {
        string addr = VnaAddress.Trim();
        if (string.IsNullOrEmpty(addr))
            addr = _config.GetVnaConnectionString();

        if (_config.VnaType == VnaConnectionType.TcpScpi && !addr.Contains(':'))
            addr = $"{_config.VnaTcpHost}:{_config.VnaTcpPort}";

        _log.Info("VNA", $"Connecting VNA: {addr}");
        bool ok = await _hw.Vna.ConnectAsync(addr);
        if (!ok)
            _log.Error("VNA", "VNA connection failed — verify IP/port, cable, and SCPI interface");
        await _hw.RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task ConnectRfSwitchAsync()
    {
        _config.RfSwitchPort = RfSwitchPort;
        _log.Info("RF", $"Connecting RF switch on {RfSwitchPort}");
        await _hw.RfSwitch.ConnectAsync(RfSwitchPort);
        await _hw.RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task ConnectAllAsync()
    {
        await ConnectTurntableAsync();
        await ConnectVnaAsync();
        await ConnectRfSwitchAsync();
    }

    /// <summary>Prototype "calibration to zero" — HOME with CW then CCW.</summary>
    [RelayCommand]
    private async Task HomeTurntableAsync()
    {
        if (_safety.IsEmergencyStopActive)
        {
            _log.Warning("TTL", "Cannot home while E-STOP is active — clear first");
            return;
        }
        _log.Info("TTL", "Calibrating reference position (zero)...");
        await _hw.Turntable.HomeAsync();
        await _hw.RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task RotateCwAsync()
    {
        await _hw.Turntable.SetSpeedAsync(SpeedDegPerSec);
        await _hw.Turntable.MoveRelativeAsync(Math.Abs(_config.MotorStepSizeDegrees));
        LiveAngle = _hw.Turntable.CurrentAngle;
    }

    [RelayCommand]
    private async Task RotateCcwAsync()
    {
        await _hw.Turntable.SetSpeedAsync(SpeedDegPerSec);
        await _hw.Turntable.MoveRelativeAsync(-Math.Abs(_config.MotorStepSizeDegrees));
        LiveAngle = _hw.Turntable.CurrentAngle;
    }

    [RelayCommand]
    private async Task SetAngleAsync()
    {
        await _hw.Turntable.SetSpeedAsync(SpeedDegPerSec);
        await _hw.Turntable.MoveToAngleAsync(TargetAngle);
        LiveAngle = _hw.Turntable.CurrentAngle;
    }

    [RelayCommand]
    private async Task EmergencyStopAsync() =>
        await _safety.ActivateEmergencyStopAsync("Hardware Control");

    [RelayCommand]
    private async Task ClearEmergencyStopAsync() =>
        await _safety.ClearEmergencyStopAsync();
}
