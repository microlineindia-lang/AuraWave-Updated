using System.IO;
using AuraWave.Analysis.Algorithms;
using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;
using AuraWave.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AuraWave.App.ViewModels.Analysis;

public partial class AnalysisViewModel : ObservableObject
{
    private readonly MeasurementSession _session;
    private readonly IDataExportService _export;
    private readonly ILogService _log;

    [ObservableProperty] private string _peakGain = "—";
    [ObservableProperty] private string _hpbw = "—";
    [ObservableProperty] private string _frontToBack = "—";
    [ObservableProperty] private string _sideLobe = "—";
    [ObservableProperty] private string _libraryStatus = "(no measurements loaded)";

    [ObservableProperty] private bool _hasVnaData;
    [ObservableProperty] private bool _hasPatternData;
    [ObservableProperty] private string _instrumentSummary = "Import Anritsu VNA CSV or run a turntable scan";
    [ObservableProperty] private string _vnaSummary = "—";
    [ObservableProperty] private string _s11Min = "—";
    [ObservableProperty] private string _s21At24Ghz = "—";
    [ObservableProperty] private string _selectedTrace = "S21";
    [ObservableProperty] private bool _overlayAllTraces = true;

    public bool ShowVnaEmptyHint => !HasVnaData;

    public IReadOnlyList<string> TraceChoices { get; } = ["S11", "S12", "S21", "S22"];

    public event Action? PatternDataChanged;
    public event Action? VnaPlotRefreshRequested;

    public AnalysisViewModel(MeasurementSession session, IDataExportService export, ILogService log)
    {
        _session = session;
        _export = export;
        _log = log;
        _session.LiveDataChanged += (_, _) => RunPatternAnalysis();
        _session.VnaDataChanged += (_, _) => ApplyVnaSnapshot();
    }

    partial void OnSelectedTraceChanged(string value) => VnaPlotRefreshRequested?.Invoke();
    partial void OnOverlayAllTracesChanged(bool value) => VnaPlotRefreshRequested?.Invoke();

    partial void OnHasVnaDataChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowVnaEmptyHint));
        VnaPlotRefreshRequested?.Invoke();
    }

    [RelayCommand]
    private async Task ImportDataAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import measurement data",
            Filter =
                "All supported|*.csv;*.s2p;*.s1p|Anritsu VNA CSV|*.csv|AuraWave pattern CSV|*.csv|Touchstone|*.s2p;*.s1p|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
        if (ext is ".s2p" or ".s1p")
        {
            await ImportTouchstoneAsync(dlg.FileName);
            return;
        }

        var lines = await File.ReadAllLinesAsync(dlg.FileName);
        if (AnritsuVnaCsvParser.LooksLikeAnritsuExport(lines))
        {
            await ImportVnaFileAsync(dlg.FileName);
            return;
        }

        await ImportPatternFileAsync(dlg.FileName);
    }

    [RelayCommand]
    private async Task ImportVnaCsvAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import Anritsu VNA CSV (MS46122B LOGMAG)",
            Filter = "VNA CSV|*.csv|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        await ImportVnaFileAsync(dlg.FileName);
    }

    private async Task ImportVnaFileAsync(string path)
    {
        var snap = await _export.ImportVnaCsvAsync(path);
        if (snap is null) return;

        _session.SetVnaSnapshot(snap);
        ApplyVnaSnapshot();
        _log.Info("IMPORT", $"VNA snapshot loaded: {Path.GetFileName(path)}");
    }

    private async Task ImportPatternFileAsync(string path)
    {
        var result = await _export.ImportPatternCsvAsync(path);
        if (result is null) return;

        _session.Clear();
        _session.ActiveResult = result;
        foreach (var p in result.DataPoints)
            _session.AddPoint(p);

        HasPatternData = true;
        LibraryStatus = $"Pattern: {result.Name} ({result.DataPoints.Count} points)";
        RunPatternAnalysis();
        PatternDataChanged?.Invoke();
    }

    private async Task ImportTouchstoneAsync(string path)
    {
        var sp = await _export.ImportTouchstoneAsync(path);
        if (sp is null) return;

        var snap = new VnaMeasurementSnapshot
        {
            SourceFile = path,
            Name = Path.GetFileNameWithoutExtension(path),
            InstrumentModel = "Touchstone"
        };
        snap.Parameters[sp.Parameter] = sp;
        _session.SetVnaSnapshot(snap);
        ApplyVnaSnapshot();
        LibraryStatus = $"Touchstone: {sp.Frequencies.Length} frequency points";
    }

    private void ApplyVnaSnapshot()
    {
        var snap = _session.VnaSnapshot;
        HasVnaData = snap is not null && snap.Parameters.Count > 0;
        if (!HasVnaData)
        {
            VnaSummary = "—";
            InstrumentSummary = "Import Anritsu VNA CSV or run a turntable scan";
            S11Min = "—";
            S21At24Ghz = "—";
            VnaPlotRefreshRequested?.Invoke();
            return;
        }

        InstrumentSummary = string.IsNullOrWhiteSpace(snap!.InstrumentModel)
            ? Path.GetFileName(snap.SourceFile)
            : $"{snap.InstrumentModel} — {Path.GetFileName(snap.SourceFile)}";

        if (snap.MeasuredAt.HasValue)
            InstrumentSummary += $" ({snap.MeasuredAt:yyyy-MM-dd})";

        string traces = string.Join(", ", snap.Parameters.Keys.OrderBy(k => k));
        VnaSummary = $"{snap.FrequencyPoints} pts · {snap.StartFreqHz / 1e9:F3}–{snap.StopFreqHz / 1e9:F3} GHz · {traces}";
        LibraryStatus = $"VNA: {snap.Name}";

        if (snap.Get("S11") is { } s11)
        {
            int minIdx = Array.IndexOf(s11.MagnitudeDb, s11.MagnitudeDb.Min());
            S11Min = $"{s11.MagnitudeDb.Min():F2} dB @ {s11.Frequencies[minIdx] / 1e9:F3} GHz";
        }
        else
            S11Min = "—";

        if (snap.Get("S21") is { } s21)
            S21At24Ghz = FormatAtFrequency(s21, 2.4e9);
        else
            S21At24Ghz = "—";

        VnaPlotRefreshRequested?.Invoke();
    }

    private static string FormatAtFrequency(SParameterData data, double targetHz)
    {
        if (data.Frequencies.Length == 0) return "—";
        int idx = 0;
        double best = double.MaxValue;
        for (int i = 0; i < data.Frequencies.Length; i++)
        {
            double d = Math.Abs(data.Frequencies[i] - targetHz);
            if (d < best)
            {
                best = d;
                idx = i;
            }
        }
        return $"{data.MagnitudeDb[idx]:F2} dB @ {data.Frequencies[idx] / 1e9:F3} GHz";
    }

    private void RunPatternAnalysis()
    {
        HasPatternData = _session.LivePoints.Count >= 3;
        if (!HasPatternData)
        {
            if (!HasVnaData)
            {
                LibraryStatus = _session.LivePoints.Count > 0
                    ? $"{_session.LivePoints.Count} points (need ≥3 for pattern metrics)"
                    : "(no measurements loaded)";
            }
            return;
        }

        if (_session.ActiveResult is null)
        {
            _session.ActiveResult = new MeasurementResult
            {
                Name = "Imported pattern",
                DataPoints = _session.LivePoints.ToList(),
                IsComplete = true
            };
        }

        var result = _session.ActiveResult;
        result.DataPoints = _session.LivePoints.ToList();
        var m = AntennaPatternAnalyzer.Analyze(result);
        result.Metrics = m;

        PeakGain = $"{m.PeakGainDbi:F2} dBi";
        Hpbw = $"{m.Hpbw:F1} °";
        FrontToBack = $"{m.FrontToBackRatio:F1} dB";
        SideLobe = $"{m.SideLobeLevel:F1} dB";
        if (!HasVnaData)
            LibraryStatus = $"Pattern — {result.DataPoints.Count} points";
        PatternDataChanged?.Invoke();
    }

    public void RefreshPlotsFromSession()
    {
        ApplyVnaSnapshot();
        RunPatternAnalysis();
    }
}
