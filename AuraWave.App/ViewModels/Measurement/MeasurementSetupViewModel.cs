using AuraWave.App.Navigation;
using AuraWave.App.ViewModels.Shell;
using AuraWave.Core.Configuration;
using AuraWave.Core.Enums;
using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AuraWave.App.ViewModels.Measurement;

public partial class MeasurementSetupViewModel : ObservableObject
{
    private readonly IScanEngine _scan;
    private readonly ILogService _log;
    private readonly IHardwareManager _hw;
    private readonly IHardwareReadinessService _readiness;
    private readonly ISafetyService _safety;
    private readonly ApplicationSettings _appSettings;

    public MainWindowViewModel? Shell { get; set; }

    [ObservableProperty] private double _startAngle = -180;
    [ObservableProperty] private double _stopAngle = 180;
    [ObservableProperty] private double _stepSize = 10;
    [ObservableProperty] private string _frequencyGHz = "2.4";
    [ObservableProperty] private int _samplesPerPoint = 3;
    [ObservableProperty] private int _selectedPlane;
    [ObservableProperty] private int _scanPlaneIndex;
    [ObservableProperty] private bool _autoHome = true;
    [ObservableProperty] private bool _autoSaveCsv = true;
    [ObservableProperty] private bool _returnToHome = true;
    [ObservableProperty] private string _estimatedPoints = "37";
    [ObservableProperty] private string _estimatedDuration = "~6 min";
    [ObservableProperty] private string _readinessMessage = "Connect and calibrate hardware before scan";

    public MeasurementSetupViewModel(
        IScanEngine scan,
        ILogService log,
        IHardwareManager hw,
        IHardwareReadinessService readiness,
        ISafetyService safety,
        ApplicationSettings appSettings)
    {
        _scan = scan;
        _log = log;
        _hw = hw;
        _readiness = readiness;
        _safety = safety;
        _appSettings = appSettings;
        ScanPlaneIndex = appSettings.DefaultScanPlane == ScanPlaneType.Elevation ? 1 : 0;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StartAngle) or nameof(StopAngle) or nameof(StepSize))
                UpdateEstimates();
        };
        UpdateEstimates();
        _ = RefreshReadinessAsync();
    }

    partial void OnStartAngleChanged(double value) => UpdateEstimates();
    partial void OnStopAngleChanged(double value) => UpdateEstimates();
    partial void OnStepSizeChanged(double value) => UpdateEstimates();

    private void UpdateEstimates()
    {
        int points = Math.Max(1, (int)Math.Round((StopAngle - StartAngle) / Math.Max(StepSize, 0.01)) + 1);
        EstimatedPoints = $"{points} points (prototype sweep)";
        int dwellMs = 200;
        double totalSec = points * (dwellMs / 1000.0 + 0.8 + SamplesPerPoint * 0.15);
        EstimatedDuration = $"~{Math.Max(1, (int)Math.Ceiling(totalSec / 60))} min";
    }

    [RelayCommand]
    private async Task RefreshReadinessAsync()
    {
        await _hw.RefreshStatusAsync();
        bool requireRf = SelectedPlane == 2;
        if (_readiness.IsReadyForScan(_hw.State, requireRf, out string reason))
            ReadinessMessage = "✓ Ready — Start will calibrate (if needed), sweep, average samples, return HOME";
        else
            ReadinessMessage = reason;
    }

    [RelayCommand]
    private async Task ValidateSetupAsync()
    {
        var config = BuildConfig();
        _log.Info("SETUP", "Prototype configuration validated (HardwareX flow)");
        _log.Info("SETUP", $"  Sweep: {config.StartAngleDeg}° to {config.StopAngleDeg}° step {config.StepSizeDeg}°");
        _log.Info("SETUP", $"  Samples/angle: {config.SamplesPerPoint} (averaged dB)");
        await RefreshReadinessAsync();
    }

    [RelayCommand]
    private async Task StartScanAsync()
    {
        if (_safety.IsEmergencyStopActive)
        {
            ReadinessMessage = "Emergency stop active — clear E-STOP first";
            return;
        }

        await RefreshReadinessAsync();
        bool requireRf = SelectedPlane == 2;
        if (!_readiness.IsReadyForScan(_hw.State, requireRf, out string reason))
        {
            _log.Error("SETUP", reason);
            ReadinessMessage = reason;
            return;
        }

        try
        {
            var config = BuildConfig();
            _log.Info("SETUP", "Starting prototype radiation pattern acquisition (Start)");
            await _scan.StartScanAsync(config);
            Shell?.NavigateRequested?.Invoke(AppRoute.LiveMeasurement);
        }
        catch (Exception ex)
        {
            _log.Error("SETUP", ex.Message);
            ReadinessMessage = ex.Message;
        }
    }

    public ScanConfiguration BuildConfig()
    {
        double freq = double.TryParse(FrequencyGHz, out double g) ? g * 1e9 : 2.4e9;
        return new ScanConfiguration
        {
            StartAngleDeg = StartAngle,
            StopAngleDeg = StopAngle,
            StepSizeDeg = StepSize,
            StartFreqHz = freq,
            StopFreqHz = freq,
            FrequencyPoints = 1,
            SamplesPerPoint = Math.Max(1, SamplesPerPoint),
            ScanPlane = ScanPlaneIndex == 1 ? ScanPlaneType.Elevation : ScanPlaneType.Azimuth,
            Polarization = SelectedPlane switch
            {
                1 => PolarizationType.HPlane,
                2 => PolarizationType.Vertical,
                _ => PolarizationType.EPlane
            },
            MeasType = MeasurementType.RadiationPattern,
            SettlingTimeMs = 200,
            TurntableSpeedDegPerSec = 5,
            AutoHomeBeforeScan = AutoHome,
            ReturnToHomeAfterScan = ReturnToHome,
            AutoSaveCsv = AutoSaveCsv,
            RequireRfSwitch = SelectedPlane == 2
        };
    }
}
