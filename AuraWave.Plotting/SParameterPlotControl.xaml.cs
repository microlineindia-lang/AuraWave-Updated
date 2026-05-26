using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using AuraWave.Core.Models;

namespace AuraWave.Plotting;

public partial class SParameterPlotControl : UserControl
{
    private static readonly Dictionary<string, string> TraceColors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["S11"] = "#EF4444",
            ["S12"] = "#F59E0B",
            ["S21"] = "#3B82F6",
            ["S22"] = "#10B981"
        };

    public SParameterPlotControl()
    {
        InitializeComponent();

        ApplyTheme();
    }

    // =====================================================
    // THEME
    // =====================================================

    private void ApplyTheme()
    {
        PlotHost.Plot.FigureBackground.Color =
            ScottPlot.Color.FromHex("#080E18");

        PlotHost.Plot.DataBackground.Color =
            ScottPlot.Color.FromHex("#0B1220");

        PlotHost.Plot.Axes.Color(
            ScottPlot.Color.FromHex("#7B8DA6"));

        PlotHost.Plot.Grid.MajorLineColor =
            ScottPlot.Color.FromHex("#1A2838");

        PlotHost.Plot.Axes.Bottom.Label.Text =
            "Frequency (GHz)";

        PlotHost.Plot.Axes.Left.Label.Text =
            "Magnitude (dB)";
    }

    // =====================================================
    // SINGLE TRACE
    // =====================================================

    public void UpdateSweep(SParameterData? data)
    {
        UpdateVnaSnapshot(
            data is null
                ? null
                : new VnaMeasurementSnapshot
                {
                    Parameters =
                    {
                        [data.Parameter] = data
                    }
                },
            data?.Parameter,
            false);
    }

    // =====================================================
    // MULTI TRACE
    // =====================================================

    public void UpdateVnaSnapshot(
        VnaMeasurementSnapshot? snapshot,
        string? highlightTrace = null,
        bool overlayAll = true)
    {
        PlotHost.Plot.Clear();

        ApplyTheme();

        if (snapshot == null ||
            snapshot.Parameters.Count == 0)
        {
            PlotHost.Refresh();
            return;
        }

        IEnumerable<KeyValuePair<string, SParameterData>>
            traces =
            overlayAll || string.IsNullOrWhiteSpace(highlightTrace)
                ? snapshot.Parameters
                : snapshot.HasParameter(highlightTrace)
                    ? new[]
                    {
                        new KeyValuePair<string, SParameterData>(
                            highlightTrace,
                            snapshot.Get(highlightTrace)!)
                    }
                    : snapshot.Parameters;

        foreach (var (name, data) in traces)
        {
            if (data.Frequencies == null ||
                data.Frequencies.Length == 0)
                continue;

            double[] ghz = data.Frequencies
                .Select(f => f / 1e9)
                .ToArray();

            var trace =
                PlotHost.Plot.Add.Scatter(
                    ghz,
                    data.MagnitudeDb);

            trace.LineWidth = 2;

            trace.MarkerSize = 0;

            trace.Color =
                ScottPlot.Color.FromHex(
                    TraceColors.GetValueOrDefault(
                        name,
                        "#3B82F6"));

            trace.LegendText = name;
        }

        PlotHost.Plot.ShowLegend();

        PlotHost.Plot.Axes.AutoScale();

        PlotHost.Refresh();
    }

    // =====================================================
    // ANGLE TRACE
    // =====================================================

    public void UpdateAngleTrace(
        IReadOnlyList<MeasurementPoint> points,
        string yLabel = "S21 (dB)")
    {
        PlotHost.Plot.Clear();

        ApplyTheme();

        if (points == null || points.Count == 0)
        {
            PlotHost.Refresh();
            return;
        }

        double[] angles = points
            .Select(p => p.AngleDegrees)
            .ToArray();

        double[] s21 = points
            .Select(p => p.S21Magnitude)
            .ToArray();

        var trace =
            PlotHost.Plot.Add.Scatter(
                angles,
                s21);

        trace.LineWidth = 2;

        trace.MarkerSize = 0;

        trace.Color =
            ScottPlot.Color.FromHex("#8B5CF6");

        trace.LegendText = yLabel;

        PlotHost.Plot.Axes.Bottom.Label.Text =
            "Angle (°)";

        PlotHost.Plot.Axes.Left.Label.Text =
            yLabel;

        PlotHost.Plot.ShowLegend();

        PlotHost.Plot.Axes.AutoScale();

        PlotHost.Refresh();
    }

    // =====================================================
    // CLEAR
    // =====================================================

    public void Clear()
    {
        PlotHost.Plot.Clear();

        ApplyTheme();

        PlotHost.Refresh();
    }
}