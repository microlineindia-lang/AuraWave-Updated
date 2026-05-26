namespace AuraWave.Core.Interfaces;

/// <summary>Global safety interlock — emergency stop halts scan, VNA, and turntable.</summary>
public interface ISafetyService
{
    bool IsEmergencyStopActive { get; }

    event EventHandler<string>? EmergencyStopActivated;
    event EventHandler? EmergencyStopCleared;

    void RegisterScanAbortHandler(Func<Task> handler);

    Task ActivateEmergencyStopAsync(string source, CancellationToken ct = default);
    Task ClearEmergencyStopAsync(CancellationToken ct = default);
}
