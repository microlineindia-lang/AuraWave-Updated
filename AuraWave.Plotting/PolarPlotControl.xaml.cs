using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using AuraWave.Core.Models;

namespace AuraWave.Plotting;

public partial class PolarPlotControl : UserControl
{
    public PolarPlotControl()
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

        PlotHost.Refresh();
    }

    // =====================================================
    // UPDATE PATTERN
    // =====================================================

    public void UpdatePattern(
        IReadOnlyList<MeasurementPoint> points,
        bool normalize = true)
    {
        PlotHost.Plot.Clear();

        ApplyTheme();

        if (points == null || points.Count == 0)
        {
            PlotHost.Refresh();
            return;
        }

        // -------------------------------------------------
        // Gain
        // -------------------------------------------------

        double[] gain = points
            .Select(p => p.GainDbi)
            .ToArray();

        // Normalize
        if (normalize)
        {
            double peak = gain.Max();

            for (int i = 0; i < gain.Length; i++)
                gain[i] -= peak;
        }

        // Clamp
        for (int i = 0; i < gain.Length; i++)
        {
            if (gain[i] < -40)
                gain[i] = -40;
        }

        // -------------------------------------------------
        // Convert to XY polar projection
        // -------------------------------------------------

        double[] xs = new double[points.Count];
        double[] ys = new double[points.Count];

        for (int i = 0; i < points.Count; i++)
        {
            // 0° at top
            double angleRad =
                (points[i].AngleDegrees - 90)
                * Math.PI / 180.0;

            // normalize radius
            double radius =
                (gain[i] + 40) / 40.0;

            xs[i] = radius * Math.Cos(angleRad);

            ys[i] = radius * Math.Sin(angleRad);
        }

        // -------------------------------------------------
        // Pattern trace
        // -------------------------------------------------

        var trace = PlotHost.Plot.Add.Scatter(xs, ys);

        trace.LineWidth = 2;

        trace.MarkerSize = 0;

        trace.Color =
            ScottPlot.Color.FromHex("#06B6D4");

        trace.LegendText = "Radiation Pattern";

        // -------------------------------------------------
        // Polar reference rings
        // -------------------------------------------------

        for (double r = 0.25; r <= 1.0; r += 0.25)
        {
            var circle = PlotHost.Plot.Add.Circle(
                0,
                0,
                r);

            circle.LineColor =
                ScottPlot.Color.FromHex("#243044");

            circle.LineWidth = 1;
        }

        // -------------------------------------------------
        // Angle lines
        // -------------------------------------------------

        for (int deg = 0; deg < 360; deg += 30)
        {
            double rad =
                (deg - 90) * Math.PI / 180.0;

            double x = Math.Cos(rad);

            double y = Math.Sin(rad);

            var line = PlotHost.Plot.Add.Line(
                0,
                0,
                x,
                y);

            line.Color =
                ScottPlot.Color.FromHex("#1A2838");

            line.LineWidth = 1;
        }

        // -------------------------------------------------
        // Styling
        // -------------------------------------------------

        PlotHost.Plot.Axes.Frameless();

        PlotHost.Plot.Axes.SetLimits(
            -1.1,
            1.1,
            -1.1,
            1.1);

        PlotHost.Plot.Axes.SquareUnits();

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