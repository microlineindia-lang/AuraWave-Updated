using System.Windows.Controls;
using AuraWave.App.Services;
using AuraWave.App.ViewModels.Analysis;
using AuraWave.Core.Services;

namespace AuraWave.App.Views.Analysis;

public partial class AnalysisPage : Page
{
    public AnalysisPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var session = AppServices.GetRequired<MeasurementSession>();
        if (DataContext is not AnalysisViewModel vm)
            return;

        void RefreshPolar()
        {
            if (session.LivePoints.Count > 0)
                AnalysisPolarPlot.UpdatePattern(session.LivePoints.ToList());
            else
                AnalysisPolarPlot.Clear();
        }

        void RefreshVna()
        {
            if (session.VnaSnapshot is null)
            {
                VnaPlot.Clear();
                return;
            }

            VnaPlot.UpdateVnaSnapshot(session.VnaSnapshot, vm.SelectedTrace, vm.OverlayAllTraces);
        }

        vm.PatternDataChanged += RefreshPolar;
        vm.VnaPlotRefreshRequested += RefreshVna;
        session.LiveDataChanged += (_, _) => RefreshPolar();
        session.VnaDataChanged += (_, _) => RefreshVna();

        vm.RefreshPlotsFromSession();
        RefreshPolar();
        RefreshVna();
    }
}
