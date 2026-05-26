using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace AuraWave.App.Views.Dialogs;

public partial class RestartNotificationWindow : Window
{
    private readonly int _totalSeconds;
    private int _remaining;
    private readonly DispatcherTimer _timer;

    public RestartNotificationWindow(int countdownSeconds, string? reason)
    {
        InitializeComponent();
        _totalSeconds = Math.Max(3, countdownSeconds);
        _remaining = _totalSeconds;

        ReasonText.Text = string.IsNullOrWhiteSpace(reason)
            ? "Your settings have been saved."
            : reason;

        UpdateCountdownUi();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        Loaded += (_, _) => _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining--;
        if (_remaining <= 0)
        {
            _timer.Stop();
            CountdownText.Text = "Restarting now…";
            ProgressBar.Value = 100;
            PerformRestart();
            return;
        }

        UpdateCountdownUi();
    }

    private void UpdateCountdownUi()
    {
        CountdownText.Text = _remaining == 1
            ? "Restarting in 1 second…"
            : $"Restarting in {_remaining} seconds…";

        double elapsed = _totalSeconds - _remaining;
        ProgressBar.Value = _totalSeconds > 0 ? elapsed / _totalSeconds * 100.0 : 0;
    }

    private static void PerformRestart()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            exe = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (!string.IsNullOrEmpty(exe))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }

        Application.Current.Shutdown();
    }
}
