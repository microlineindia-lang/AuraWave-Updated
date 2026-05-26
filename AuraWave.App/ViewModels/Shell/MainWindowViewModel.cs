using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using AuraWave.App.Navigation;
using AuraWave.Core.Configuration;
using AuraWave.Core.Enums;
using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AuraWave.App.ViewModels.Shell;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IHardwareManager _hardware;
    private readonly ILogService _log;
    private readonly IProjectService _project;
    private readonly IScanEngine _scan;
    private readonly ISafetyService _safety;
    private readonly HardwareConfiguration _hwConfig;
    private readonly ApplicationSettings _appSettings;
    private readonly DispatcherTimer _statusTimer;

    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(34, 197, 94));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(239, 68, 68));
    private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(251, 146, 60));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(100, 116, 139));

    [ObservableProperty] private string _projectName = "Untitled Measurement";
    [ObservableProperty] private string _scanState = "Idle";
    [ObservableProperty] private string _footerStatus = "Connect hardware on Hardware Control";
    [ObservableProperty] private string _consoleText = string.Empty;
    [ObservableProperty] private string _runModeBadge = "PHYSICAL HARDWARE";
    [ObservableProperty] private string _consoleToggleLabel = "Hide Console";
    [ObservableProperty] private string _operatorName = "Lab User";
    [ObservableProperty] private string _chamberId = "Chamber-01";
    [ObservableProperty] private string _safetyInterlockText = "Interlocks: Ready";
    [ObservableProperty] private string _estopCircuitText = "E-Stop circuit: Armed";
    [ObservableProperty] private bool _isEstopActive;

    [ObservableProperty] private string _vnaStatus = "Disconnected";
    [ObservableProperty] private string _vnaDetail = "Not connected";
    [ObservableProperty] private Brush _vnaStatusBrush = Red;

    [ObservableProperty] private string _turntableStatus = "Disconnected";
    [ObservableProperty] private string _turntableDetail = "COM port required";
    [ObservableProperty] private Brush _turntableStatusBrush = Red;

    [ObservableProperty] private string _motorStatus = "Offline";
    [ObservableProperty] private string _motorDetail = "Stepper driver";
    [ObservableProperty] private Brush _motorStatusBrush = Muted;

    [ObservableProperty] private string _rfSwitchStatus = "Disconnected";
    [ObservableProperty] private string _rfSwitchDetail = "Polarization";
    [ObservableProperty] private Brush _rfSwitchStatusBrush = Red;

    [ObservableProperty] private string _chamberStatus = "—";
    [ObservableProperty] private string _chamberDetail = "—";
    [ObservableProperty] private Brush _chamberStatusBrush = Muted;

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public Action<AppRoute>? NavigateRequested { get; set; }

    public MainWindowViewModel(
        IHardwareManager hardware,
        ILogService log,
        IProjectService project,
        IScanEngine scan,
        ISafetyService safety,
        HardwareConfiguration hwConfig,
        ApplicationSettings appSettings)
    {
        _hardware = hardware;
        _log = log;
        _project = project;
        _scan = scan;
        _safety = safety;
        _hwConfig = hwConfig;
        _appSettings = appSettings;

        OperatorName = _appSettings.OperatorName;
        ChamberId = _appSettings.ChamberId;

        _safety.EmergencyStopActivated += (_, _) =>
            System.Windows.Application.Current.Dispatcher.Invoke(RefreshSafetyUi);
        _safety.EmergencyStopCleared += (_, _) =>
            System.Windows.Application.Current.Dispatcher.Invoke(RefreshSafetyUi);

        _hardware.StateChanged += (_, state) =>
            System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyHardwareState(state));

        _scan.StateUpdated += (_, state) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ScanState = state.Phase switch
                {
                    ScanPhase.Running => $"Running {state.ProgressPercent:F0}%",
                    ScanPhase.Homing => "Calibrating",
                    ScanPhase.ReturningHome => "Returning home",
                    ScanPhase.Initializing => "Initializing",
                    ScanPhase.Paused => "Paused",
                    ScanPhase.Complete => "Complete",
                    ScanPhase.Error => "Error",
                    ScanPhase.EmergencyStop => "E-STOP",
                    ScanPhase.Aborting => "Aborting",
                    _ => "Idle"
                };
                if (state.Phase != ScanPhase.EmergencyStop)
                    FooterStatus = ScanState;
            });
        };

        _log.EntryAdded += (_, _) =>
            System.Windows.Application.Current.Dispatcher.Invoke(RefreshConsoleFromLog);

        _project.ProjectChanged += (_, p) => ProjectName = p.Name;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += async (_, _) => await _hardware.RefreshStatusAsync();
        _statusTimer.Start();

        _ = _hardware.RefreshStatusAsync();
        RefreshConsoleFromLog();
        RefreshSafetyUi();

        _log.Info("SYSTEM", "AuraWave prototype measurement suite — physical hardware");
        _log.Info("SYSTEM", "Workflow: Connect → Calibrate (HOME) → Start scan → 360° pattern with averaged VNA reads");
    }

    public event Action? ConsoleUpdated;

    public void SetConsoleExpanded(bool expanded) =>
        ConsoleToggleLabel = expanded ? "Hide Console" : "Show Console";

    private void RefreshSafetyUi()
    {
        IsEstopActive = _safety.IsEmergencyStopActive;
        SafetyInterlockText = IsEstopActive ? "Interlocks: E-STOP ACTIVE" : "Interlocks: Ready";
        EstopCircuitText = IsEstopActive ? "E-Stop circuit: TRIPPED" : "E-Stop circuit: Armed";
        if (IsEstopActive)
        {
            FooterStatus = "E-STOP ACTIVE — clear before resuming";
            ScanState = "E-STOP";
        }
    }

    private void RefreshConsoleFromLog()
    {
        LogEntries.Clear();
        foreach (var entry in _log.Entries)
            LogEntries.Add(entry);

        ConsoleText = string.Join(Environment.NewLine,
            _log.Entries.Select(e => e.FormattedLine));
        ConsoleUpdated?.Invoke();
    }

    private void ApplyHardwareState(SystemHardwareState state)
    {
        HardwareState = state;
        RefreshSafetyUi();

        if (state.Vna.IsConnected)
        {
            VnaStatus = "Connected";
            VnaDetail = string.IsNullOrEmpty(state.Vna.InstrumentId)
                ? state.Vna.ResourceAddress
                : state.Vna.InstrumentId;
            VnaStatusBrush = Green;
        }
        else
        {
            VnaStatus = "Disconnected";
            VnaDetail = _hwConfig.GetVnaConnectionString();
            VnaStatusBrush = Red;
        }

        if (state.Turntable.IsConnected)
        {
            TurntableStatus = state.Turntable.EmergencyStop ? "E-STOP" :
                state.Turntable.IsMoving ? "Moving" :
                state.Turntable.IsHomed ? $"{state.Turntable.CurrentAngleDeg:F1}°" : "Not calibrated";
            TurntableDetail = state.Turntable.PortName;
            TurntableStatusBrush = state.Turntable.EmergencyStop ? Red :
                state.Turntable.IsHomed ? Green : Orange;
            MotorStatus = state.Turntable.EmergencyStop ? "HALTED" :
                state.Turntable.IsMoving ? "Running" : "Ready";
            MotorStatusBrush = state.Turntable.EmergencyStop ? Red : Green;
        }
        else
        {
            TurntableStatus = "Disconnected";
            TurntableDetail = _hwConfig.TurntablePort;
            TurntableStatusBrush = Red;
            MotorStatus = "Offline";
            MotorStatusBrush = Muted;
        }

        if (state.RfSwitch.IsConnected)
        {
            RfSwitchStatus = state.RfSwitch.ActivePath.ToString();
            RfSwitchDetail = state.RfSwitch.PortName;
            RfSwitchStatusBrush = Green;
        }
        else
        {
            RfSwitchStatus = "Disconnected";
            RfSwitchDetail = _hwConfig.RfSwitchPort;
            RfSwitchStatusBrush = Red;
        }

        ChamberStatus = state.Chamber.DoorLocked ? "Locked" : "Open";
        ChamberDetail = $"{state.Chamber.TemperatureCelsius:F1}°C";
        ChamberStatusBrush = state.Chamber.DoorLocked ? Green : Orange;

        if (!IsEstopActive && state.Vna.IsConnected && state.Turntable.IsConnected && state.Turntable.IsHomed)
            FooterStatus = "Ready — prototype scan can start";
        else if (!IsEstopActive && (!state.Vna.IsConnected || !state.Turntable.IsConnected))
            FooterStatus = "Connect VNA and turntable";
        else if (!IsEstopActive && !state.Turntable.IsHomed)
            FooterStatus = "Calibrate turntable (HOME)";
    }

    [ObservableProperty]
    private SystemHardwareState _hardwareState = new();

    [RelayCommand]
    private void NewProject()
    {
        _project.NewProject("Untitled Measurement");
        ProjectName = _project.CurrentProject!.Name;
        _log.Info("PROJECT", "New measurement project created");
    }

    [RelayCommand]
    private void ClearConsole()
    {
        _log.Clear();
        LogEntries.Clear();
        ConsoleText = string.Empty;
        _log.Info("SYSTEM", "Console cleared");
    }

    [RelayCommand]
    private async Task EmergencyStopAsync() =>
        await _safety.ActivateEmergencyStopAsync("Shell sidebar");

    [RelayCommand]
    private async Task ClearEmergencyStopAsync() =>
        await _safety.ClearEmergencyStopAsync();
}
