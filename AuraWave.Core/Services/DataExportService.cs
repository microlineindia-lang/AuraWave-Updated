using System.Globalization;
using System.Text;
using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;

namespace AuraWave.Core.Services;

public sealed class DataExportService : IDataExportService
{
    private readonly ILogService _log;

    public DataExportService(ILogService log) => _log = log;

    public string DefaultExportDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "AuraWave",
                "Exports");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public Task<string> ExportPatternCsvAsync(MeasurementResult result, string? directory = null, CancellationToken ct = default)
    {
        string name = string.IsNullOrWhiteSpace(result.Name)
            ? $"pattern_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            : Path.ChangeExtension(result.Name, ".csv");
        return ExportPatternCsvAsync(result.DataPoints, result.ScanConfig, name, directory, ct);
    }

    public async Task<string> ExportPatternCsvAsync(
        IReadOnlyList<MeasurementPoint> points,
        ScanConfiguration? config,
        string fileName,
        string? directory = null,
        CancellationToken ct = default)
    {
        directory ??= DefaultExportDirectory;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);

        var sb = new StringBuilder();
        sb.AppendLine("# AuraWave Pattern Export");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:O}");
        if (config is not null)
        {
            sb.AppendLine($"# StartAngle_deg,{config.StartAngleDeg}");
            sb.AppendLine($"# StopAngle_deg,{config.StopAngleDeg}");
            sb.AppendLine($"# Step_deg,{config.StepSizeDeg}");
            sb.AppendLine($"# CenterFreq_Hz,{(config.StartFreqHz + config.StopFreqHz) / 2.0}");
            sb.AppendLine($"# Polarization,{config.Polarization}");
        }
        sb.AppendLine("Angle_deg,Frequency_Hz,S21_dB,S21_phase_deg,S11_dB,Gain_dBi,Timestamp_UTC");

        foreach (var p in points)
        {
            sb.AppendLine(string.Join(",",
                p.AngleDegrees.ToString("F4", CultureInfo.InvariantCulture),
                p.FrequencyHz.ToString("F2", CultureInfo.InvariantCulture),
                p.S21Magnitude.ToString("F4", CultureInfo.InvariantCulture),
                p.S21Phase.ToString("F4", CultureInfo.InvariantCulture),
                p.S11Magnitude.ToString("F4", CultureInfo.InvariantCulture),
                p.GainDbi.ToString("F4", CultureInfo.InvariantCulture),
                p.Timestamp.ToString("O", CultureInfo.InvariantCulture)));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), ct);
        _log.Info("EXPORT", $"Pattern CSV saved: {path} ({points.Count} points)");
        return path;
    }

    public async Task<string> ExportHtmlReportAsync(MeasurementResult result, string? directory = null, CancellationToken ct = default)
    {
        directory ??= DefaultExportDirectory;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory,
            $"{SanitizeFileName(result.Name)}_report_{DateTime.Now:yyyyMMdd_HHmmss}.html");

        var m = result.Metrics;
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
        sb.AppendLine("<title>AuraWave Measurement Report</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;background:#0a0f18;color:#e2e8f0;padding:32px}");
        sb.AppendLine("h1{color:#06b6d4}table{border-collapse:collapse;width:100%;margin-top:16px}");
        sb.AppendLine("td,th{border:1px solid #1e293b;padding:8px 12px;text-align:left}");
        sb.AppendLine("th{background:#111827}</style></head><body>");
        sb.AppendLine("<h1>AuraWave Antenna Radiation Pattern Report</h1>");
        sb.AppendLine($"<p><strong>Measurement:</strong> {Escape(result.Name)}<br/>");
        sb.AppendLine($"<strong>Created:</strong> {result.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC<br/>");
        sb.AppendLine($"<strong>Points:</strong> {result.DataPoints.Count}</p>");

        if (m is not null)
        {
            sb.AppendLine("<h2>Calculated Metrics</h2><table>");
            sb.AppendLine($"<tr><th>Peak Gain</th><td>{m.PeakGainDbi:F2} dBi @ {m.PeakGainAngle:F1}°</td></tr>");
            sb.AppendLine($"<tr><th>HPBW</th><td>{m.Hpbw:F1}°</td></tr>");
            sb.AppendLine($"<tr><th>FNBW</th><td>{m.Fnbw:F1}°</td></tr>");
            sb.AppendLine($"<tr><th>Side Lobe Level</th><td>{m.SideLobeLevel:F1} dB</td></tr>");
            sb.AppendLine($"<tr><th>Front-to-Back</th><td>{m.FrontToBackRatio:F1} dB</td></tr>");
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<h2>Sample Data (first 20 points)</h2><table><tr><th>Angle (°)</th><th>S21 (dB)</th><th>Gain (dBi)</th></tr>");
        foreach (var p in result.DataPoints.Take(20))
            sb.AppendLine($"<tr><td>{p.AngleDegrees:F2}</td><td>{p.S21Magnitude:F2}</td><td>{p.GainDbi:F2}</td></tr>");
        sb.AppendLine("</table><p><em>Export full CSV from Reports for complete dataset.</em></p>");
        sb.AppendLine("</body></html>");

        await File.WriteAllTextAsync(path, sb.ToString(), ct);
        _log.Info("EXPORT", $"HTML report saved: {path}");
        return path;
    }

    public async Task<string> ExportTouchstoneS2PAsync(MeasurementResult result, string? directory = null, CancellationToken ct = default)
    {
        directory ??= DefaultExportDirectory;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory,
            $"{SanitizeFileName(result.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.s2p");

        double freq = result.ScanConfig.StartFreqHz > 0
            ? (result.ScanConfig.StartFreqHz + result.ScanConfig.StopFreqHz) / 2.0
            : 2.4e9;

        var sb = new StringBuilder();
        sb.AppendLine("! AuraWave pattern export (angle-sweep as single-frequency S2P-style)");
        sb.AppendLine($"! FREQ {freq / 1e9:F6} GHz");
        sb.AppendLine("! Format: RI");
        sb.AppendLine($"! {result.DataPoints.Count} angle samples");
        sb.AppendLine($"# Hz S RI R 50");

        foreach (var p in result.DataPoints)
        {
            double mag = Math.Pow(10, p.S21Magnitude / 20.0);
            double phaseRad = p.S21Phase * Math.PI / 180.0;
            double re = mag * Math.Cos(phaseRad);
            double im = mag * Math.Sin(phaseRad);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0:E6} {1:E6} {2:E6} {3:E6} {4:E6} {5:E6} {6:E6} {7:E6}",
                freq, re, im, 0, 0, 0, 0, 0));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), ct);
        _log.Info("EXPORT", $"Touchstone export saved: {path}");
        return path;
    }

    public async Task<VnaMeasurementSnapshot?> ImportVnaCsvAsync(string filePath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        if (!AnritsuVnaCsvParser.LooksLikeAnritsuExport(lines))
        {
            _log.Warning("IMPORT", $"Not an Anritsu VNA LOGMAG CSV: {Path.GetFileName(filePath)}");
            return null;
        }

        try
        {
            var snap = AnritsuVnaCsvParser.Parse(lines, filePath);
            _log.Info("IMPORT",
                $"VNA CSV: {snap.Parameters.Count} traces, {snap.FrequencyPoints} points, " +
                $"{snap.StartFreqHz / 1e9:F3}–{snap.StopFreqHz / 1e9:F3} GHz");
            return snap;
        }
        catch (Exception ex)
        {
            _log.Error("IMPORT", $"VNA CSV parse failed: {ex.Message}");
            return null;
        }
    }

    public async Task<MeasurementResult?> ImportPatternCsvAsync(string filePath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        if (AnritsuVnaCsvParser.LooksLikeAnritsuExport(lines))
        {
            _log.Warning("IMPORT", "File is Anritsu VNA sweep CSV — use VNA import, not pattern import.");
            return null;
        }

        var points = new List<MeasurementPoint>();
        ScanConfiguration config = new();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                ParseMetadata(line, config);
                continue;
            }
            if (line.StartsWith("Angle", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 3) continue;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double angle))
                continue;

            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double freq);
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double s21);

            var point = new MeasurementPoint
            {
                AngleDegrees = angle,
                FrequencyHz = freq > 0 ? freq : 2.4e9,
                S21Magnitude = s21,
                GainDbi = s21
            };
            if (parts.Length > 3 &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double s21Ph))
                point.S21Phase = s21Ph;
            if (parts.Length > 4 &&
                double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double s11))
                point.S11Magnitude = s11;
            if (parts.Length > 5 &&
                double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double gain))
                point.GainDbi = gain;

            points.Add(point);
        }

        if (points.Count == 0)
        {
            _log.Warning("IMPORT", $"No data points in CSV: {filePath}");
            return null;
        }

        _log.Info("IMPORT", $"Loaded {points.Count} points from {filePath}");
        return new MeasurementResult
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            DataPoints = points,
            ScanConfig = config,
            IsComplete = true
        };
    }

    public async Task<SParameterData?> ImportTouchstoneAsync(string filePath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var freqs = new List<double>();
        var magDb = new List<double>();
        var phaseDeg = new List<double>();
        string format = "RI";

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith('!') || line.StartsWith('#'))
            {
                if (line.Contains("RI", StringComparison.OrdinalIgnoreCase)) format = "RI";
                if (line.Contains("MA", StringComparison.OrdinalIgnoreCase)) format = "MA";
                if (line.Contains("DB", StringComparison.OrdinalIgnoreCase)) format = "DB";
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double freq))
                continue;

            freqs.Add(freq);
            if (format == "DB" && parts.Length >= 3)
            {
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double db);
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double ph);
                magDb.Add(db);
                phaseDeg.Add(ph);
            }
            else if (parts.Length >= 3)
            {
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double re);
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double im);
                double mag = Math.Sqrt(re * re + im * im);
                magDb.Add(20 * Math.Log10(Math.Max(mag, 1e-12)));
                phaseDeg.Add(Math.Atan2(im, re) * 180 / Math.PI);
            }
        }

        if (freqs.Count == 0)
        {
            _log.Warning("IMPORT", $"No frequency points in Touchstone: {filePath}");
            return null;
        }

        _log.Info("IMPORT", $"Touchstone loaded: {freqs.Count} points from {Path.GetFileName(filePath)}");
        return new SParameterData
        {
            Frequencies = freqs.ToArray(),
            MagnitudeDb = magDb.ToArray(),
            PhaseDeg = phaseDeg.ToArray(),
            Parameter = "S21",
            Timestamp = DateTime.UtcNow
        };
    }

    private static void ParseMetadata(string line, ScanConfiguration config)
    {
        if (!line.StartsWith('#')) return;
        var body = line.TrimStart('#').Trim();
        var idx = body.IndexOf(',');
        if (idx < 0) return;
        var key = body[..idx].Trim();
        var val = body[(idx + 1)..].Trim();
        if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return;

        switch (key.ToUpperInvariant())
        {
            case "STARTANGLE_DEG": config.StartAngleDeg = d; break;
            case "STOPANGLE_DEG": config.StopAngleDeg = d; break;
            case "STEP_DEG": config.StepSizeDeg = d; break;
            case "CENTERFREQ_HZ": config.StartFreqHz = config.StopFreqHz = d; break;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "measurement" : name;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
