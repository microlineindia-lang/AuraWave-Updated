using AuraWave.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AuraWave.App.ViewModels.Calibration;

public partial class CalibrationViewModel : ObservableObject
{
    private readonly IHardwareManager _hw;
    private readonly ILogService _log;

    [ObservableProperty] private string _vnaCalStatus = "Not Calibrated";
    [ObservableProperty] private string _positionZero = "Unset";
    [ObservableProperty] private string _referenceAntenna = "Not Selected";
    [ObservableProperty] private string _calibrationLog = string.Empty;

    public CalibrationViewModel(IHardwareManager hw, ILogService log)
    {
        _hw = hw;
        _log = log;
        _log.EntryAdded += (_, e) =>
        {
            if (e.Source.Contains("CAL", StringComparison.OrdinalIgnoreCase))
                CalibrationLog += e.FormattedLine + Environment.NewLine;
        };
    }

    [RelayCommand]
    private async Task StartVnaCalibrationAsync()
    {
        _log.Info("CAL", "Starting VNA calibration sequence...");
        await _hw.Vna.ResetAsync();
        await _hw.Vna.SendRawScpiAsync("SENS:CORR:COLL:GUID");
        VnaCalStatus = "In Progress";
    }

    [RelayCommand]
    private async Task HomeTurntableAsync()
    {
        await _hw.Turntable.HomeAsync();
        PositionZero = "Set @ 0°";
    }
}
