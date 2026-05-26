using System.Windows.Controls;
using AuraWave.App.ViewModels.Measurement;

namespace AuraWave.App.Views.Measurement;

public partial class LiveMeasurementPage : Page
{
    public LiveMeasurementPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is LiveMeasurementViewModel vm)
            vm.AttachPlots(PolarPlot, S21Plot, null!, Mesh3D);
    }
}
