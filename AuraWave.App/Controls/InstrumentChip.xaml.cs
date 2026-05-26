using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuraWave.App.Controls;

public partial class InstrumentChip : UserControl
{
    public InstrumentChip() => InitializeComponent();

    public static readonly DependencyProperty DeviceNameProperty =
        DependencyProperty.Register(nameof(DeviceName), typeof(string), typeof(InstrumentChip), new PropertyMetadata("Device"));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(string), typeof(InstrumentChip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(string), typeof(InstrumentChip), new PropertyMetadata("—"));

    public static readonly DependencyProperty StatusBrushProperty =
        DependencyProperty.Register(nameof(StatusBrush), typeof(Brush), typeof(InstrumentChip),
            new PropertyMetadata(Brushes.Gray));

    public string DeviceName
    {
        get => (string)GetValue(DeviceNameProperty);
        set => SetValue(DeviceNameProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public Brush StatusBrush
    {
        get => (Brush)GetValue(StatusBrushProperty);
        set => SetValue(StatusBrushProperty, value);
    }
}
