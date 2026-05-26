using AuraWave.Core.Interfaces;

namespace AuraWave.Core.Services;

public sealed class SafetyService : ISafetyService
{
    private readonly IHardwareManager _hw;
    private readonly ILogService _log;
    private Func<Task>? _abortScanHandler;

    public bool IsEmergencyStopActive { get; private set; }

    public event EventHandler<string>? EmergencyStopActivated;
    public event EventHandler? EmergencyStopCleared;

    public SafetyService(IHardwareManager hw, ILogService log)
    {
        _hw = hw;
        _log = log;
    }

    public void RegisterScanAbortHandler(Func<Task> handler) => _abortScanHandler = handler;

    public async Task ActivateEmergencyStopAsync(string source, CancellationToken ct = default)
    {
        if (IsEmergencyStopActive)
        {
            _log.Warning("SAFETY", $"E-STOP already active (additional trigger: {source})");
            return;
        }

        IsEmergencyStopActive = true;
        _log.Warning("SAFETY", "════════════════════════════════════════");
        _log.Warning("SAFETY", $"EMERGENCY STOP — source: {source}");
        _log.Warning("SAFETY", "Halting scan, VNA measurement, and turntable motion");
        _log.Warning("SAFETY", "════════════════════════════════════════");

        if (_abortScanHandler is not null)
        {
            try { await _abortScanHandler(); }
            catch (Exception ex) { _log.Error("SAFETY", $"Scan abort: {ex.Message}"); }
        }

        try { await _hw.Vna.AbortMeasurementAsync(ct); }
        catch (Exception ex) { _log.Error("SAFETY", $"VNA abort: {ex.Message}"); }

        try { await _hw.Turntable.EmergencyStopAsync(); }
        catch (Exception ex) { _log.Error("SAFETY", $"Turntable E-STOP: {ex.Message}"); }

        try { await _hw.Turntable.StopAsync(); }
        catch { /* redundant safety */ }

        await _hw.RefreshStatusAsync();
        EmergencyStopActivated?.Invoke(this, source);
    }

    public async Task ClearEmergencyStopAsync(CancellationToken ct = default)
    {
        if (!IsEmergencyStopActive) return;

        _log.Info("SAFETY", "Clearing emergency stop — operator confirmed safe to continue");
        await _hw.Turntable.ClearEmergencyStopAsync(ct);
        IsEmergencyStopActive = false;
        await _hw.RefreshStatusAsync();
        EmergencyStopCleared?.Invoke(this, EventArgs.Empty);
        _log.Info("SAFETY", "E-STOP cleared — home turntable before next scan");
    }
}
