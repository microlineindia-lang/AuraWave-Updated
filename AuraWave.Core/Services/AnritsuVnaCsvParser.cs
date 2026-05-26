using System.Globalization;
using System.Text.RegularExpressions;
using AuraWave.Core.Models;

namespace AuraWave.Core.Services;

/// <summary>
/// Parses Anritsu ShockLine / MS46122B style CSV exports:
/// !PARAMETER rows, PNT,FREQ1.GHZ,LOGMAG1,... header, frequency sweep LOGMAG traces.
/// </summary>
public static class AnritsuVnaCsvParser
{
    private static readonly string[] DefaultTraceNames = ["S11", "S12", "S21", "S22"];

    public static bool LooksLikeAnritsuExport(IEnumerable<string> lines) =>
        lines.Any(l => l.StartsWith("!PARAMETER:", StringComparison.OrdinalIgnoreCase))
        || lines.Any(l => l.Contains("LOGMAG", StringComparison.OrdinalIgnoreCase)
                         && l.StartsWith("PNT,", StringComparison.OrdinalIgnoreCase));

    public static VnaMeasurementSnapshot Parse(IReadOnlyList<string> lines, string sourceFile)
    {
        var snap = new VnaMeasurementSnapshot
        {
            SourceFile = sourceFile,
            Name = Path.GetFileNameWithoutExtension(sourceFile)
        };

        if (lines.Count > 0 && !lines[0].StartsWith('!'))
            snap.InstrumentModel = lines[0].Split(',')[0].Trim();

        foreach (var line in lines)
        {
            if (!line.StartsWith('!')) continue;
            var body = line[1..].Trim();
            if (body.StartsWith("PARAMETER:", StringComparison.OrdinalIgnoreCase))
            {
                snap.TraceNames = ParseParameterNames(body);
            }
            else if (body.Contains('/') && body.Contains(':'))
            {
                var m = Regex.Match(body, @"(\d{1,2})/(\d{1,2})/(\d{4})");
                if (m.Success && DateTime.TryParse(m.Value, CultureInfo.InvariantCulture, out var dt))
                    snap.MeasuredAt = dt;
            }
        }

        string[] traceNames = snap.TraceNames.Length > 0 ? snap.TraceNames : DefaultTraceNames;

        int headerIdx = -1;
        string[] headerParts = Array.Empty<string>();
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("PNT,", StringComparison.OrdinalIgnoreCase))
            {
                headerIdx = i;
                headerParts = t.Split(',');
                break;
            }
        }

        if (headerIdx < 0)
            throw new InvalidDataException("Anritsu VNA CSV: missing PNT,FREQ... header row.");

        var traceColumns = MapTraceColumns(headerParts, traceNames.Length);

        var buckets = traceNames.ToDictionary(
            n => n,
            _ => new List<(double FreqHz, double MagDb)>(),
            StringComparer.OrdinalIgnoreCase);

        for (int i = headerIdx + 1; i < lines.Count; i++)
        {
            var row = lines[i].Trim();
            if (string.IsNullOrEmpty(row) || row.StartsWith('!'))
                continue;

            var cols = row.Split(',');
            if (cols.Length < 3)
                continue;

            for (int t = 0; t < traceNames.Length && t < traceColumns.Count; t++)
            {
                var (freqCol, magCol) = traceColumns[t];
                if (freqCol >= cols.Length || magCol >= cols.Length)
                    continue;

                if (!TryParseDouble(cols[freqCol], out double freqGhz))
                    continue;
                if (!TryParseDouble(cols[magCol], out double magDb))
                    continue;

                double freqHz = freqGhz < 1000 ? freqGhz * 1e9 : freqGhz;
                buckets[traceNames[t]].Add((freqHz, magDb));
            }
        }

        foreach (var name in traceNames)
        {
            if (!buckets.TryGetValue(name, out var pts) || pts.Count == 0)
                continue;

            var ordered = pts.OrderBy(p => p.FreqHz).ToList();
            snap.Parameters[name] = new SParameterData
            {
                Parameter = name,
                Frequencies = ordered.Select(p => p.FreqHz).ToArray(),
                MagnitudeDb = ordered.Select(p => p.MagDb).ToArray(),
                Timestamp = snap.MeasuredAt ?? DateTime.UtcNow
            };
        }

        if (snap.Parameters.Count == 0)
            throw new InvalidDataException("Anritsu VNA CSV: no trace data rows parsed.");

        return snap;
    }

    private static string[] ParseParameterNames(string parameterLine)
    {
        var after = parameterLine.Split(':', 2);
        if (after.Length < 2) return DefaultTraceNames;

        return after[1].Split(',')
            .Select(s => s.Trim())
            .Where(s => s.StartsWith('S') && s.Length >= 3)
            .Select(s => s.ToUpperInvariant())
            .ToArray();
    }

    private static List<(int FreqCol, int MagCol)> MapTraceColumns(string[] header, int traceCount)
    {
        var list = new List<(int, int)>();
        for (int t = 0; t < traceCount; t++)
        {
            int freqCol = 1 + t * 2;
            int magCol = 2 + t * 2;
            list.Add((freqCol, magCol));
        }
        return list;
    }

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
