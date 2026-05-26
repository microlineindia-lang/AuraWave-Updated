using System.Windows;
using System.Windows.Controls;

namespace AuraWave.App.Controls;

public partial class PlotSurface : UserControl
{
    public PlotSurface() => InitializeComponent();

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PlotSurface), new PropertyMetadata("Plot"));

    public static readonly DependencyProperty PlotKindProperty =
        DependencyProperty.Register(nameof(PlotKind), typeof(string), typeof(PlotSurface), new PropertyMetadata("2D"));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(PlotSurface), new PropertyMetadata("Plot engine — stage 2"));

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(PlotSurface), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string PlotKind
    {
        get => (string)GetValue(PlotKindProperty);
        set => SetValue(PlotKindProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }
}
