using AuraWave.App.Services;
using AuraWave.Core.Configuration;
using AuraWave.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AuraWave.App.ViewModels.Settings;

public enum SettingsSection
{
    HardwareProfile,
    Communication,
    Logging,
    Appearance,
    UserPreferences
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly HardwareConfiguration _hw;
    private readonly ApplicationSettings _app;
    private readonly ILogService _log;
    private readonly ISettingsPersistenceService _persistence;
    private readonly IApplicationRestartService _restart;

    [ObservableProperty] private SettingsSection _selectedSection = SettingsSection.HardwareProfile;

    [ObservableProperty] private string _turntablePort = "COM3";
    [ObservableProperty] private int _turntableBaud = 115200;
    [ObservableProperty] private int _vnaConnectionIndex = 1;
    [ObservableProperty] private string _vnaTcpHost = "192.168.1.100";
    [ObservableProperty] private int _vnaTcpPort = 5025;
    [ObservableProperty] private string _vnaSerialPort = "COM5";
    [ObservableProperty] private int _vnaSerialBaud = 115200;
    [ObservableProperty] private string _rfSwitchPort = "COM4";
    [ObservableProperty] private int _rfSwitchBaud = 9600;
    [ObservableProperty] private double _motorStepSizeDegrees = 0.9;
    [ObservableProperty] private double _defaultTurntableSpeed = 5.0;

    [ObservableProperty] private string _operatorName = "Lab User";
    [ObservableProperty] private string _chamberId = "Chamber-01";
    [ObservableProperty] private double _farFieldMinDistanceM = 1.0;
    [ObservableProperty] private int _defaultScanPlaneIndex;

    [ObservableProperty] private int _logLevelIndex = 0;
    [ObservableProperty] private bool _logScpiToConsole = true;
    [ObservableProperty] private bool _logSerialToConsole = true;

    [ObservableProperty] private bool _useCompactUi;
    [ObservableProperty] private string _saveStatus = string.Empty;

    public bool IsHardwareProfileVisible => SelectedSection == SettingsSection.HardwareProfile;
    public bool IsCommunicationVisible => SelectedSection == SettingsSection.Communication;
    public bool IsLoggingVisible => SelectedSection == SettingsSection.Logging;
    public bool IsAppearanceVisible => SelectedSection == SettingsSection.Appearance;
    public bool IsUserPreferencesVisible => SelectedSection == SettingsSection.UserPreferences;

    public SettingsViewModel(
        HardwareConfiguration hw,
        ApplicationSettings app,
        ILogService log,
        ISettingsPersistenceService persistence,
        IApplicationRestartService restart)
    {
        _hw = hw;
        _app = app;
        _log = log;
        _persistence = persistence;
        _restart = restart;
        LoadFromConfig();
    }

    partial void OnSelectedSectionChanged(SettingsSection value)
    {
        OnPropertyChanged(nameof(IsHardwareProfileVisible));
        OnPropertyChanged(nameof(IsCommunicationVisible));
        OnPropertyChanged(nameof(IsLoggingVisible));
        OnPropertyChanged(nameof(IsAppearanceVisible));
        OnPropertyChanged(nameof(IsUserPreferencesVisible));
    }

    [RelayCommand]
    private void SelectSection(string section)
    {
        if (Enum.TryParse<SettingsSection>(section, out var s))
            SelectedSection = s;
    }

    private void LoadFromConfig()
    {
        TurntablePort = _hw.TurntablePort;
        TurntableBaud = _hw.TurntableBaud;
        VnaTcpHost = _hw.VnaTcpHost;
        VnaTcpPort = _hw.VnaTcpPort;
        VnaSerialPort = _hw.VnaSerialPort;
        VnaSerialBaud = _hw.VnaSerialBaud;
        RfSwitchPort = _hw.RfSwitchPort;
        RfSwitchBaud = _hw.RfSwitchBaud;
        MotorStepSizeDegrees = _hw.MotorStepSizeDegrees;
        DefaultTurntableSpeed = _hw.DefaultTurntableSpeedDegPerSec;

        VnaConnectionIndex = _hw.VnaType switch
        {
            VnaConnectionType.SerialScpi => 1,
            _ => 0
        };

        OperatorName = _app.OperatorName;
        ChamberId = _app.ChamberId;
        FarFieldMinDistanceM = _app.FarFieldMinDistanceM;
        LogScpiToConsole = _app.LogScpiToConsole;
        LogSerialToConsole = _app.LogSerialToConsole;
        DefaultScanPlaneIndex = _app.DefaultScanPlane == ScanPlaneType.Elevation ? 1 : 0;
        LogLevelIndex = _app.LogLevel switch
        {
            "Information" => 1,
            "Warning" => 2,
            "Error" => 3,
            _ => 0
        };
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        _hw.VnaType = VnaConnectionIndex == 1 ? VnaConnectionType.SerialScpi : VnaConnectionType.TcpScpi;
        _hw.TurntablePort = TurntablePort;
        _hw.TurntableBaud = TurntableBaud;
        _hw.VnaTcpHost = VnaTcpHost;
        _hw.VnaTcpPort = VnaTcpPort;
        _hw.VnaSerialPort = VnaSerialPort;
        _hw.VnaSerialBaud = VnaSerialBaud;
        _hw.RfSwitchPort = RfSwitchPort;
        _hw.RfSwitchBaud = RfSwitchBaud;
        _hw.MotorStepSizeDegrees = MotorStepSizeDegrees;
        _hw.DefaultTurntableSpeedDegPerSec = DefaultTurntableSpeed;

        _app.OperatorName = OperatorName;
        _app.ChamberId = ChamberId;
        _app.FarFieldMinDistanceM = FarFieldMinDistanceM;
        _app.LogScpiToConsole = LogScpiToConsole;
        _app.LogSerialToConsole = LogSerialToConsole;
        _app.DefaultScanPlane = DefaultScanPlaneIndex == 1 ? ScanPlaneType.Elevation : ScanPlaneType.Azimuth;
        _app.LogLevel = LogLevelIndex switch
        {
            1 => "Information",
            2 => "Warning",
            3 => "Error",
            _ => "Debug"
        };

        await _persistence.SaveAsync(_hw, _app);
        SaveStatus = "Settings saved. AuraWave will restart soon to apply changes.";
        _log.Info("SETTINGS", $"Saved — restart scheduled (TTL={TurntablePort}, VNA={_hw.GetVnaConnectionString()})");

        _restart.ScheduleRestart(
            countdownSeconds: 5,
            reason: "Settings were saved. AuraWave will restart to apply hardware and communication changes.");
    }
}
