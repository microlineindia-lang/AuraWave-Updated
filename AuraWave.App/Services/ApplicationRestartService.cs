using System.Windows;
using AuraWave.App.Views.Dialogs;

namespace AuraWave.App.Services;

public sealed class ApplicationRestartService : IApplicationRestartService
{
    public void ScheduleRestart(int countdownSeconds = 5, string? reason = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new RestartNotificationWindow(countdownSeconds, reason);
            dialog.Show();
        });
    }
}
