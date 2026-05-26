using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AuraWave.App.Navigation;
using AuraWave.App.Services;
using AuraWave.App.ViewModels.Shell;
using AuraWave.App.Views.Analysis;
using AuraWave.App.Views.Calibration;
using AuraWave.App.Views.Dashboard;
using AuraWave.App.Views.Hardware;
using AuraWave.App.Views.Measurement;
using AuraWave.App.Views.Reports;
using AuraWave.App.Views.Settings;

namespace AuraWave.App.Views.Shell;

public partial class MainWindow : Window
{
    private readonly Dictionary<AppRoute, Button> _navButtons;
    private readonly MainWindowViewModel _shellVm;
    private bool _consoleExpanded = true;
    private const double ConsoleExpandedHeight = 200;

    public MainWindow()
    {
        InitializeComponent();
        _shellVm = AppServices.GetRequired<MainWindowViewModel>();
        DataContext = _shellVm;
        _shellVm.NavigateRequested = NavigateTo;
        _shellVm.ConsoleUpdated += ScrollConsoleToEnd;

        _navButtons = new Dictionary<AppRoute, Button>
        {
            [AppRoute.Dashboard] = NavDashboard,
            [AppRoute.Hardware] = NavHardware,
            [AppRoute.MeasurementSetup] = NavSetup,
            [AppRoute.LiveMeasurement] = NavLive,
            [AppRoute.Analysis] = NavAnalysis,
            [AppRoute.Calibration] = NavCalibration,
            [AppRoute.Reports] = NavReports,
            [AppRoute.Settings] = NavSettings
        };

        KeyDown += MainWindow_KeyDown;
        NavigateTo(AppRoute.Dashboard);
        ApplyConsoleVisibility();
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Oem3 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleConsole();
            e.Handled = true;
        }
    }

    private void NavigateTo(AppRoute route)
    {
        SetActiveNav(route);

        Page page = route switch
        {
            AppRoute.Dashboard => CreatePage<DashboardPage, ViewModels.Dashboard.DashboardViewModel>(vm =>
            {
                if (vm is ViewModels.Dashboard.DashboardViewModel dash)
                    dash.Shell = _shellVm;
            }),
            AppRoute.Hardware => CreatePage<HardwareControlPage, ViewModels.Hardware.HardwareControlViewModel>(),
            AppRoute.MeasurementSetup => CreatePage<MeasurementSetupPage, ViewModels.Measurement.MeasurementSetupViewModel>(vm =>
            {
                if (vm is ViewModels.Measurement.MeasurementSetupViewModel setup)
                    setup.Shell = _shellVm;
            }),
            AppRoute.LiveMeasurement => CreatePage<LiveMeasurementPage, ViewModels.Measurement.LiveMeasurementViewModel>(),
            AppRoute.Analysis => CreatePage<AnalysisPage, ViewModels.Analysis.AnalysisViewModel>(),
            AppRoute.Calibration => CreatePage<CalibrationPage, ViewModels.Calibration.CalibrationViewModel>(),
            AppRoute.Reports => CreatePage<ReportsPage, ViewModels.Reports.ReportsViewModel>(),
            AppRoute.Settings => CreatePage<SettingsPage, ViewModels.Settings.SettingsViewModel>(),
            _ => CreatePage<DashboardPage, ViewModels.Dashboard.DashboardViewModel>()
        };

        MainFrame.Navigate(page);
    }

    private static Page CreatePage<TPage, TVm>(Action<TVm>? configure = null)
        where TPage : Page, new()
        where TVm : class
    {
        var page = new TPage();
        var vm = AppServices.GetRequired<TVm>();
        configure?.Invoke(vm);
        page.DataContext = vm;
        return page;
    }

    private void SetActiveNav(AppRoute route)
    {
        foreach (var pair in _navButtons)
            pair.Value.Tag = pair.Key == route ? "Active" : null;
    }

    private void ToggleConsole_Click(object sender, RoutedEventArgs e) => ToggleConsole();

    private void ToggleConsole()
    {
        _consoleExpanded = !_consoleExpanded;
        ApplyConsoleVisibility();
    }

    private void ApplyConsoleVisibility()
    {
        if (_consoleExpanded)
        {
            ConsoleRow.Height = new GridLength(ConsoleExpandedHeight);
            ConsolePanel.Visibility = Visibility.Visible;
        }
        else
        {
            ConsoleRow.Height = new GridLength(0);
            ConsolePanel.Visibility = Visibility.Collapsed;
        }

        _shellVm.SetConsoleExpanded(_consoleExpanded);
    }

    private void NavDashboard_Click(object sender, RoutedEventArgs e) => NavigateTo(AppRoute.Dashboard);
    private void NavHardware_Click(object sender, RoutedEventArgs e) => NavigateTo(AppRoute.Hardware);
    private void NavSetup_Click(object sender, RoutedEventArgs e) => NavigateTo(AppRoute.MeasurementSetup);
    private void NavLive_Click(object sender, RoutedEventArgs e) => NavigateTo(AppRoute.LiveMeasurement);
    private void NavAnalysis_Click(object sender, RoutedEventArgs e) => NavigateTo(AppRoute.Analysis);
    private void NavCalibration_Click(object sender, RoutedEventArgs e) => NavigateTo(AppRoute.Calibration);
    private void NavReports_Click(object sender, RoutedEventArgs e) => NavigateTo(AppRoute.Reports);
    private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(AppRoute.Settings);

    private void ClearConsole_Click(object sender, RoutedEventArgs e) =>
        _shellVm.ClearConsoleCommand.Execute(null);

    private void ScrollConsoleToEnd()
    {
        if (ConsoleOutput is null) return;
        ConsoleOutput.ScrollToEnd();
        ConsoleOutput.CaretIndex = ConsoleOutput.Text.Length;
    }

    private void ConsoleOutput_TextChanged(object sender, TextChangedEventArgs e)
    {
        ConsoleOutput.ScrollToEnd();
    }
}
