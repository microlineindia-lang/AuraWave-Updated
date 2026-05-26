using System.Collections.ObjectModel;
using AuraWave.Analysis.Algorithms;
using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;
using AuraWave.Core.Services;
using AuraWave.Plotting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AuraWave.App.ViewModels.Measurement;

public record LiveDataRow(string Angle, string S21, string Gain);

public partial class LiveMeasurementViewModel : ObservableObject
{
    private readonly IScanEngine _scan;
    private readonly IHardwareManager _hw;
    private readonly MeasurementSession _session;
    private readonly ILogService _log;
    private readonly ISafetyService _safety;

    private PolarPlotControl? _polar;
    private SParameterPlotControl? _s21Plot;
    private SParameterPlotControl? _s11Plot;
    private RadiationMesh3DControl? _mesh3d;

    [ObservableProperty] private string _currentAngle = "0.00";
    [ObservableProperty] private string _frequency = "2.400";
    [ObservableProperty] private string _liveS21 = "—";
    [ObservableProperty] private string _progress = "0";
    [ObservableProperty] private string _eta = "--:--";
    [ObservableProperty] private bool _isScanning;

    public ObservableCollection<LiveDataRow> DataRows { get; } = new();

    public LiveMeasurementViewModel(
        IScanEngine scan,
        IHardwareManager hw,
        MeasurementSession session,
        ILogService log,
        ISafetyService safety)
    {
        _scan = scan;
        _hw = hw;
        _session = session;
        _log = log;
        _safety = safety;

        _scan.StateUpdated += (_, state) => UpdateFromState(state);
        _scan.PointAcquired += OnPointAcquired;
        _scan.ScanCompleted += OnScanCompleted;
        _session.LiveDataChanged += (_, _) => RefreshPlots();
        _session.VnaDataChanged += (_, _) => RefreshPlots();
    }

    public void AttachPlots(
        PolarPlotControl polar,
        SParameterPlotControl s21,
        SParameterPlotControl? s11,
        RadiationMesh3DControl mesh3d)
    {
        _polar = polar;
        _s21Plot = s21;
        _s11Plot = s11;
        _mesh3d = mesh3d;
        RefreshPlots();
    }

    [RelayCommand]
    private async Task PauseAsync() => await _scan.PauseAsync();

    [RelayCommand]
    private async Task ResumeAsync() => await _scan.ResumeAsync();

    [RelayCommand]
    private async Task AbortAsync() => await _scan.AbortAsync();

    [RelayCommand]
    private async Task EmergencyStopAsync() =>
        await _safety.ActivateEmergencyStopAsync("Live Measurement");

    [RelayCommand]
    private async Task RefreshS11Async()
    {
        try
        {
            var s11 = await _hw.Vna.ReadS11Async();
            _session.SetS11Sweep(s11);
            _s11Plot?.UpdateSweep(s11);
        }
        catch (Exception ex)
        {
            _log.Error("LIVE", ex.Message);
        }
    }

    private void UpdateFromState(LiveScanState state)
    {
        IsScanning = state.Phase is Core.Enums.ScanPhase.Running
            or Core.Enums.ScanPhase.Initializing
            or Core.Enums.ScanPhase.Homing
            or Core.Enums.ScanPhase.ReturningHome;
        CurrentAngle = $"{state.CurrentAngle:F2}";
        Frequency = $"{state.CurrentFreqHz / 1e9:F3}";
        LiveS21 = $"{state.CurrentS21:F1}";
        Progress = $"{state.ProgressPercent:F0}";
        Eta = state.EstimatedRemaining.ToString(@"mm\:ss");
    }

    private void OnPointAcquired(object? sender, MeasurementPoint point)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            DataRows.Add(new LiveDataRow(
                $"{point.AngleDegrees:F2}",
                $"{point.S21Magnitude:F2}",
                $"{point.GainDbi:F2}"));
            while (DataRows.Count > 500) DataRows.RemoveAt(0);
        });
        RefreshPlots();
    }

    private void RefreshPlots()
    {
        var points = _session.LivePoints.ToList();
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (points.Count > 0)
            {
                _polar?.UpdatePattern(points);
                _s21Plot?.UpdateAngleTrace(points);
                _mesh3d?.UpdatePattern(points);
            }
            else if (_session.VnaSnapshot is not null)
            {
                _s21Plot?.UpdateVnaSnapshot(_session.VnaSnapshot, "S21", overlayAll: true);
            }
        });
    }

    private void OnScanCompleted(object? sender, MeasurementResult result)
    {
        result.Metrics = AntennaPatternAnalyzer.Analyze(result);
        _log.Info("LIVE", $"Scan complete — HPBW={result.Metrics?.Hpbw:F1}° Peak={result.Metrics?.PeakGainDbi:F1} dBi");
        RefreshPlots();
        _ = RefreshS11Async();
    }
}
