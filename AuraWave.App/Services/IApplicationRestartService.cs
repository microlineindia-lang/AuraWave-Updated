namespace AuraWave.App.Services;

public interface IApplicationRestartService
{
    /// <summary>Shows restart UI and relaunches AuraWave after the countdown.</summary>
    void ScheduleRestart(int countdownSeconds = 5, string? reason = null);
}
