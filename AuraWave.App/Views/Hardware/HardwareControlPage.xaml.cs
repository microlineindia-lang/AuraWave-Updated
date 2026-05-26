using System.Windows.Controls;
using AuraWave.App.ViewModels.Hardware;
using AuraWave.Core.Models;

namespace AuraWave.App.Views.Hardware;

public partial class HardwareControlPage : Page
{
    public HardwareControlPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not HardwareControlViewModel vm) return;

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HardwareControlViewModel.LiveAngle))
                PositionPreview.UpdatePattern(new[]
                {
                    new MeasurementPoint { AngleDegrees = vm.LiveAngle, GainDbi = 0, S21Magnitude = 0 }
                });
        };
    }
}
