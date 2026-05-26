using AuraWave.App.Navigation;
using AuraWave.App.ViewModels.Shell;
using AuraWave.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AuraWave.App.ViewModels.Dashboard;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IHardwareManager _hw;
    private readonly IScanEngine _scan;
    private readonly ISafetyService _safety;

    public MainWindowViewModel? Shell { get; set; }

    [ObservableProperty] private string _currentAngle = "0.00°";
    [ObservableProperty] private string _frequency = "2.400 GHz";
    [ObservableProperty] private double _scanProgress;

    public DashboardViewModel(IHardwareManager hw, IScanEngine scan, ISafetyService safety)
    {
        _hw = hw;
        _scan = scan;
        _safety = safety;
        _hw.Turntable.PositionChanged += (_, a) => CurrentAngle = $"{a:F2}°";
        _scan.StateUpdated += (_, s) => ScanProgress = s.ProgressPercent;
    }

    [RelayCommand]
    private void StartScan() =>
        Shell?.NavigateRequested?.Invoke(AppRoute.MeasurementSetup);

    [RelayCommand]
    private async Task HomeTurntableAsync()
    {
        if (_safety.IsEmergencyStopActive) return;
        await _hw.Turntable.HomeAsync();
        await _hw.RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task SwitchEPlaneAsync()
    {
        await _hw.RfSwitch.SetPathAsync(Core.Enums.PolarizationType.EPlane);
        await _hw.RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task SwitchHPlaneAsync()
    {
        await _hw.RfSwitch.SetPathAsync(Core.Enums.PolarizationType.HPlane);
        await _hw.RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task EmergencyStopAsync() =>
        await _safety.ActivateEmergencyStopAsync("Dashboard");
}
