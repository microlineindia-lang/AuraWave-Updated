using AuraWave.Core.Interfaces;
using AuraWave.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Diagnostics;

namespace AuraWave.App.ViewModels.Reports;

public partial class ReportsViewModel : ObservableObject
{
    private readonly MeasurementSession _session;
    private readonly IDataExportService _export;
    private readonly ILogService _log;

    [ObservableProperty] private string _previewStatus = "No report generated";
    [ObservableProperty] private bool _includeSetup = true;
    [ObservableProperty] private bool _includePlots = true;
    [ObservableProperty] private bool _includeMetrics = true;

    public ReportsViewModel(MeasurementSession session, IDataExportService export, ILogService log)
    {
        _session = session;
        _log = log;
        _export = export;
        _session.LiveDataChanged += (_, _) => RefreshPreview();
    }

    private void RefreshPreview()
    {
        int n = _session.LivePoints.Count;
        PreviewStatus = n > 0 ? $"Ready — {n} measurement points" : "No measurement data — run a scan first";
    }

    [RelayCommand]
    private void GenerateReport()
    {
        RefreshPreview();
        if (_session.ActiveResult is null || _session.LivePoints.Count == 0)
        {
            _log.Warning("REPORT", "No measurement data to report");
            return;
        }
        _log.Info("REPORT", $"Report preview: {_session.LivePoints.Count} points, metrics available");
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var result = GetResult();
        if (result is null) return;
        string path = await _export.ExportPatternCsvAsync(result);
        PreviewStatus = $"CSV exported: {path}";
        OpenFolder(path);
    }

    [RelayCommand]
    private async Task ExportHtmlAsync()
    {
        var result = GetResult();
        if (result is null) return;
        string path = await _export.ExportHtmlReportAsync(result);
        PreviewStatus = $"HTML report: {path}";
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { OpenFolder(path); }
    }

    [RelayCommand]
    private async Task ExportTouchstoneAsync()
    {
        var result = GetResult();
        if (result is null) return;
        string path = await _export.ExportTouchstoneS2PAsync(result);
        PreviewStatus = $"Touchstone: {path}";
        OpenFolder(path);
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        var result = GetResult();
        if (result is null) return;
        string path = await _export.ExportHtmlReportAsync(result);
        PreviewStatus = $"Printable report (HTML): {path} — open in browser and Print to PDF";
        _log.Info("REPORT", "Use browser Print → Save as PDF for formal PDF output");
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { OpenFolder(path); }
    }

    private Core.Models.MeasurementResult? GetResult()
    {
        if (_session.ActiveResult is null || _session.LivePoints.Count == 0)
        {
            _log.Warning("EXPORT", "No active measurement — complete a scan first");
            PreviewStatus = "No data — run Measurement Setup → Start Scan";
            return null;
        }
        _session.ActiveResult.DataPoints = _session.LivePoints.ToList();
        return _session.ActiveResult;
    }

    private static void OpenFolder(string filePath)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch { /* ignore */ }
    }
}
