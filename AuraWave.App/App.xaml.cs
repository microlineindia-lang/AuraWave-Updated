using AuraWave.App.Services;
using AuraWave.App.ViewModels.Shell;
using AuraWave.App.Views.Shell;
using AuraWave.Core.Configuration;
using AuraWave.Core.Interfaces;
using AuraWave.Core.Services;
using AuraWave.Hardware.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using System.Windows;

namespace AuraWave.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load configuration early so logging follows settings in appsettings.json
        var earlyConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        var appSettings = new Core.Configuration.ApplicationSettings();
        earlyConfig.GetSection("Application").Bind(appSettings);

        // Determine Serilog level from configuration
        var level = Serilog.Events.LogEventLevel.Debug;
        if (!string.IsNullOrWhiteSpace(appSettings.LogLevel) &&
            Enum.TryParse<Serilog.Events.LogEventLevel>(appSettings.LogLevel, true, out var parsed))
            level = parsed;

        var loggerCfg = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "logs", "aurawave_.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14);

        // Add console output if configured (SCPI or serial logging to console)
        if (appSettings.LogScpiToConsole || appSettings.LogSerialToConsole)
        {
            loggerCfg = loggerCfg.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff}] [{Level,-7}] [{Source,-12}] {Message:lj}{NewLine}{Exception}");
        }

        Log.Logger = loggerCfg.CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureAppConfiguration(cfg =>
            {
                cfg.SetBasePath(AppContext.BaseDirectory);
                cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureServices((ctx, services) =>
            {
                var hw = new HardwareConfiguration();
                ctx.Configuration.GetSection("Hardware").Bind(hw);
                services.AddSingleton(hw);

                var appSettings = new ApplicationSettings();
                ctx.Configuration.GetSection("Application").Bind(appSettings);
                services.AddSingleton(appSettings);

                services.AddSingleton<ILogService, LogService>();
                services.AddSingleton<IDataExportService, DataExportService>();
                services.AddSingleton<ISettingsPersistenceService, SettingsPersistenceService>();
                services.AddSingleton<IHardwareReadinessService, HardwareReadinessService>();
                services.AddSingleton<ISafetyService, SafetyService>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IProjectService, ProjectService>();
                services.AddSingleton<MeasurementSession>();
                services.AddSingleton<IScanEngine, ScanEngine>();
                services.AddSingleton<IHardwareManager, HardwareManager>();

                services.AddAuraWaveHardware();

                services.AddSingleton<IApplicationRestartService, ApplicationRestartService>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddTransient<ViewModels.Dashboard.DashboardViewModel>();
                services.AddTransient<ViewModels.Hardware.HardwareControlViewModel>();
                services.AddTransient<ViewModels.Measurement.MeasurementSetupViewModel>();
                services.AddTransient<ViewModels.Measurement.LiveMeasurementViewModel>();
                services.AddTransient<ViewModels.Analysis.AnalysisViewModel>();
                services.AddTransient<ViewModels.Calibration.CalibrationViewModel>();
                services.AddTransient<ViewModels.Reports.ReportsViewModel>();
                services.AddTransient<ViewModels.Settings.SettingsViewModel>();
            })
            .Build();

        await _host.StartAsync();
        AppServices.Initialize(_host.Services);

        var mainVm = _host.Services.GetRequiredService<MainWindowViewModel>();
        _host.Services.GetRequiredService<IProjectService>().NewProject("Untitled Measurement");

        var log = _host.Services.GetRequiredService<ILogService>();
        log.Info("SYSTEM", "AuraWave — physical hardware mode (no simulation)");
        log.Info("SYSTEM", "Connect VNA, Arduino turntable, and RF switch on Hardware Control before scanning");

        var mainWin = new MainWindow { DataContext = mainVm };
        MainWindow = mainWin;
        mainWin.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            var hw = _host.Services.GetService<IHardwareManager>();
            if (hw is not null)
            {
                await hw.Turntable.DisconnectAsync();
                await hw.Vna.DisconnectAsync();
                await hw.RfSwitch.DisconnectAsync();
            }

            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
