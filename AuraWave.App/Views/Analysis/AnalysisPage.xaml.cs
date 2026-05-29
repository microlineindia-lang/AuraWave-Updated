//using System.Windows.Controls;
//using AuraWave.App.Services;
//using AuraWave.App.ViewModels.Analysis;
//using AuraWave.Core.Services;

//namespace AuraWave.App.Views.Analysis;

//public partial class AnalysisPage : Page
//{
//    public AnalysisPage()
//    {
//        InitializeComponent();
//        Loaded += OnLoaded;
//    }

//    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
//    {
//        var session = AppServices.GetRequired<MeasurementSession>();
//        if (DataContext is not AnalysisViewModel vm)
//            return;

//        void RefreshPolar()
//        {
//            if (session.LivePoints.Count > 0)
//                AnalysisPolarPlot.UpdatePattern(session.LivePoints.ToList());
//            else
//                AnalysisPolarPlot.Clear();
//        }

//        void RefreshVna()
//        {
//            if (session.VnaSnapshot is null)
//            {
//                VnaPlot.Clear();
//                return;
//            }

//            VnaPlot.UpdateVnaSnapshot(session.VnaSnapshot, vm.SelectedTrace, vm.OverlayAllTraces);
//        }

//        vm.PatternDataChanged += RefreshPolar;
//        vm.VnaPlotRefreshRequested += RefreshVna;
//        session.LiveDataChanged += (_, _) => RefreshPolar();
//        session.VnaDataChanged += (_, _) => RefreshVna();

//        vm.RefreshPlotsFromSession();
//        RefreshPolar();
//        RefreshVna();
//    }
//}


using System;
using System.Linq;
using System.Windows.Controls;
using AuraWave.App.Services;
using AuraWave.App.ViewModels.Analysis;
using AuraWave.Core.Services;

namespace AuraWave.App.Views.Analysis;

public partial class AnalysisPage : Page
{
    private MeasurementSession? _session;
    private AnalysisViewModel? _vm;

    public AnalysisPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _session = AppServices.GetRequired<MeasurementSession>();

        if (DataContext is not AnalysisViewModel vm)
            return;

        _vm = vm;

        vm.PatternDataChanged += RefreshPolar;
        vm.VnaPlotRefreshRequested += RefreshVna;
        _session.LiveDataChanged += OnLiveDataChanged;
        _session.VnaDataChanged += OnVnaDataChanged;

        vm.RefreshPlotsFromSession();
        RefreshPolar();
        RefreshVna();
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PatternDataChanged -= RefreshPolar;
            _vm.VnaPlotRefreshRequested -= RefreshVna;
        }

        if (_session is not null)
        {
            _session.LiveDataChanged -= OnLiveDataChanged;
            _session.VnaDataChanged -= OnVnaDataChanged;
        }
    }

    private void OnLiveDataChanged(object? sender, EventArgs e)
    {
        RefreshPolar();
    }

    private void OnVnaDataChanged(object? sender, EventArgs e)
    {
        RefreshVna();
    }

    private void RefreshPolar()
    {
        if (_session is null)
            return;

        if (_session.LivePoints.Count > 0)
            AnalysisPolarPlot.UpdatePattern(_session.LivePoints.ToList());
        else
            AnalysisPolarPlot.Clear();
    }

    private void RefreshVna()
    {
        if (_session is null || _vm is null)
            return;

        if (_session.VnaSnapshot is null)
        {
            VnaPlot.Clear();
            return;
        }

        VnaPlot.UpdateVnaSnapshot(_session.VnaSnapshot, _vm.SelectedTrace, _vm.OverlayAllTraces);
    }
}
