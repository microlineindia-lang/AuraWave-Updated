using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AuraWave.Core.Models;
using HelixToolkit.Wpf;

namespace AuraWave.Plotting;

public partial class RadiationMesh3DControl : UserControl
{
    private PointsVisual3D? _pointsVisual;

    public RadiationMesh3DControl() => InitializeComponent();

    public void UpdatePattern(IReadOnlyList<MeasurementPoint> points)
    {
        if (_pointsVisual is not null)
            Viewport.Children.Remove(_pointsVisual);

        if (points.Count < 3)
            return;

        double peak = points.Max(p => p.GainDbi);
        var positions = new Point3DCollection();
        var colors = new List<Color>();

        foreach (var p in points)
        {
            double rad = p.AngleDegrees * Math.PI / 180.0;
            double norm = Math.Pow(10, (p.GainDbi - peak) / 20.0);
            double r = 0.5 + norm * 1.5;
            positions.Add(new Point3D(
                r * Math.Cos(rad),
                r * Math.Sin(rad),
                0));
            colors.Add(MapColor(norm));
        }

        // Close loop
        var first = positions[0];
        positions.Add(first);

        _pointsVisual = new PointsVisual3D
        {
            Size = 6,
            Color = Colors.Cyan,
            Points = positions
        };
        Viewport.Children.Add(_pointsVisual);

        var line = new LinesVisual3D
        {
            Color = Colors.Cyan,
            Thickness = 2
        };
        for (int i = 0; i < positions.Count - 1; i++)
        {
            line.Points.Add(positions[i]);
            line.Points.Add(positions[i + 1]);
        }
        Viewport.Children.Add(line);

        Viewport.ZoomExtents();
    }

    private static Color MapColor(double norm)
    {
        byte g = (byte)(80 + norm * 175);
        return Color.FromRgb(6, g, 212);
    }

    public void Clear()
    {
        Viewport.Children.Clear();
        Viewport.Children.Add(new DefaultLights());
    }
}
