using AuraWave.Core.Enums;
using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AuraWave.Core.Services
{
    /// <summary>
    /// Prototype measurement sequence (HardwareX 2025):
    /// Calibrate reference → 360° sweep → at each step: move, average VNA samples, store (angle, dB), plot.
    /// </summary>
    public sealed class ScanEngine : IScanEngine
    {
        private readonly IHardwareManager _hw;
        private readonly ILogService _log;
        private readonly MeasurementSession _session;
        private readonly IDataExportService _export;
        private readonly IHardwareReadinessService _readiness;
        private readonly ISafetyService _safety;
        private readonly ILogger<ScanEngine> _logger;

        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _pauseSem = new(0, 1);
        private bool _paused;
        private readonly LiveScanState _state = new();

        public ScanPhase CurrentPhase => _state.Phase;
        public LiveScanState State => _state;
        public MeasurementResult? ActiveResult { get; private set; }

        public event EventHandler<LiveScanState>? StateUpdated;
        public event EventHandler<MeasurementPoint>? PointAcquired;
        public event EventHandler<MeasurementResult>? ScanCompleted;
        public event EventHandler<string>? ErrorOccurred;

        public ScanEngine(
            IHardwareManager hw,
            ILogService log,
            MeasurementSession session,
            IDataExportService export,
            IHardwareReadinessService readiness,
            ISafetyService safety,
            ILogger<ScanEngine> logger)
        {
            _hw = hw;
            _log = log;
            _session = session;
            _export = export;
            _readiness = readiness;
            _safety = safety;
            _logger = logger;
            _safety.RegisterScanAbortHandler(() => AbortAsync());
        }

        public async Task<MeasurementResult> StartScanAsync(
            ScanConfiguration config, CancellationToken ct = default)
        {
            if (_safety.IsEmergencyStopActive)
                throw new InvalidOperationException("Emergency stop is active. Clear E-STOP before starting a scan.");

            if (_state.Phase == ScanPhase.Running)
                throw new InvalidOperationException("Scan already in progress.");

            await _hw.RefreshStatusAsync();
            if (!_readiness.IsReadyForScan(_hw.State, config.RequireRfSwitch, out string blockReason))
            {
                _log.Error("SCAN", blockReason);
                ErrorOccurred?.Invoke(this, blockReason);
                throw new InvalidOperationException(blockReason);
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var result = new MeasurementResult
            {
                ScanConfig = config,
                Name = $"Scan_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            ActiveResult = result;
            _session.Clear();
            _session.ActiveResult = result;

            _ = ExecuteScanAsync(config, result, _cts.Token);
            return result;
        }

        public Task PauseAsync()
        {
            if (_state.Phase != ScanPhase.Running) return Task.CompletedTask;
            _paused = true;
            _state.Phase = ScanPhase.Paused;
            _log.Info("SCAN", "Scan paused — turntable holds position");
            UpdateState();
            return Task.CompletedTask;
        }

        public Task ResumeAsync()
        {
            if (_state.Phase != ScanPhase.Paused) return Task.CompletedTask;
            _paused = false;
            _state.Phase = ScanPhase.Running;
            _pauseSem.Release();
            _log.Info("SCAN", "Scan resumed");
            UpdateState();
            return Task.CompletedTask;
        }

        public async Task AbortAsync()
        {
            _log.Warning("SCAN", "Scan abort requested — stopping turntable");
            _cts?.Cancel();
            if (_paused) _pauseSem.Release();
            await _hw.Turntable.StopAsync();
            _state.Phase = _safety.IsEmergencyStopActive ? ScanPhase.EmergencyStop : ScanPhase.Aborting;
            UpdateState();
        }

        private async Task ExecuteScanAsync(
            ScanConfiguration config,
            MeasurementResult result,
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            double centerFreq = (config.StartFreqHz + config.StopFreqHz) / 2.0;

            try
            {
                _state.Phase = ScanPhase.Initializing;
                _state.TotalSteps = config.TotalPoints;
                UpdateState();

                _log.Info("SCAN", "════════════════════════════════════════");
                _log.Info("SCAN", "Prototype pattern acquisition started");
                _log.Info("SCAN", "  1) Calibrate turntable (HOME / reference 0°)");
                _log.Info("SCAN", "  2) Full rotation sweep with step triggers");
                _log.Info("SCAN", $"  3) Average {config.SamplesPerPoint} VNA samples per angle → dB");
                _log.Info("SCAN", $"  Plane: {config.ScanPlane} | {config.StartAngleDeg}° → {config.StopAngleDeg}° step {config.StepSizeDeg}°");
                _log.Info("SCAN", $"  RF: {centerFreq / 1e9:F4} GHz | {config.Polarization}");
                _log.Info("SCAN", "════════════════════════════════════════");

                await _hw.Vna.SetStartFrequencyAsync(config.StartFreqHz, ct);
                await _hw.Vna.SetStopFrequencyAsync(config.StopFreqHz, ct);
                await _hw.Vna.SetSweepPointsAsync(config.FrequencyPoints, ct);
                await _hw.Vna.SetIfBandwidthAsync(config.IfBandwidthHz, ct);
                await _hw.Vna.SetPortPowerAsync(1, config.PowerLevelDbm, ct);

                await _hw.RfSwitch.SetPathAsync(config.Polarization, ct);
                await _hw.Turntable.SetSpeedAsync(config.TurntableSpeedDegPerSec, ct);

                if (config.AutoHomeBeforeScan)
                {
                    _state.Phase = ScanPhase.Homing;
                    UpdateState();
                    _log.Info("TTL", "Calibrating to reference position (prototype zero)...");
                    await _hw.Turntable.HomeAsync(ct);
                }

                _log.Info("TTL", $"Moving to sweep start {config.StartAngleDeg:F2}°");
                await _hw.Turntable.MoveToAngleAsync(config.StartAngleDeg, ct);

                _state.Phase = ScanPhase.Running;
                UpdateState();
                _log.Info("SCAN", "Synchronized sweep running — press E-STOP anytime to halt all motion");

                double angle = config.StartAngleDeg;
                int stepIndex = 0;

                while (angle <= config.StopAngleDeg + 1e-6 && !ct.IsCancellationRequested)
                {
                    if (_safety.IsEmergencyStopActive)
                    {
                        ct.ThrowIfCancellationRequested();
                    }

                    if (_paused)
                        await _pauseSem.WaitAsync(ct);

                    ct.ThrowIfCancellationRequested();

                    await _hw.Turntable.MoveToAngleAsync(angle, ct);

                    if (config.SettlingTimeMs > 0)
                        await Task.Delay(config.SettlingTimeMs, ct);

                    double s21 = await ReadAveragedS21Async(config, centerFreq, ct);

                    var point = new MeasurementPoint
                    {
                        AngleDegrees = angle,
                        FrequencyHz = centerFreq,
                        GainDbi = s21,
                        S21Magnitude = s21,
                        Timestamp = DateTime.UtcNow
                    };

                    result.DataPoints.Add(point);
                    _session.AddPoint(point);

                    _state.CurrentStep = ++stepIndex;
                    _state.CurrentAngle = angle;
                    _state.CurrentFreqHz = centerFreq;
                    _state.CurrentGain = s21;
                    _state.CurrentS21 = s21;
                    _state.ElapsedTime = sw.Elapsed;

                    double rate = stepIndex / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
                    int remaining = config.TotalPoints - stepIndex;
                    _state.EstimatedRemaining = rate > 0
                        ? TimeSpan.FromSeconds(remaining / rate)
                        : TimeSpan.Zero;

                    UpdateState();
                    PointAcquired?.Invoke(this, point);

                    _log.Info("SCAN",
                        $"  ✓ [{stepIndex}/{config.TotalPoints}] θ={angle:F2}°  P_avg={s21:F2} dB ({config.SamplesPerPoint} samples)");

                    angle += config.StepSizeDeg;
                }

                if (!ct.IsCancellationRequested)
                {
                    result.IsComplete = true;

                    if (config.ReturnToHomeAfterScan)
                    {
                        _state.Phase = ScanPhase.ReturningHome;
                        UpdateState();
                        _log.Info("TTL", "Sweep complete — returning to reference (CW then CCW HOME)");
                        await _hw.Turntable.HomeAsync(ct);
                    }

                    _state.Phase = ScanPhase.Complete;
                    _state.CurrentStep = config.TotalPoints;
                    UpdateState();

                    _log.Info("SCAN", $"Pattern acquisition COMPLETE: {result.DataPoints.Count} points in {sw.Elapsed:mm\\:ss}");

                    if (config.AutoSaveCsv)
                    {
                        try
                        {
                            string path = await _export.ExportPatternCsvAsync(result, ct: ct);
                            _log.Info("EXPORT", $"Data stored: {path}");
                        }
                        catch (Exception ex)
                        {
                            _log.Error("EXPORT", $"Auto-save failed: {ex.Message}");
                        }
                    }

                    ScanCompleted?.Invoke(this, result);
                }
                else
                {
                    _state.Phase = _safety.IsEmergencyStopActive ? ScanPhase.EmergencyStop : ScanPhase.Idle;
                    UpdateState();
                    _log.Warning("SCAN", _safety.IsEmergencyStopActive
                        ? "Scan halted by EMERGENCY STOP"
                        : "Scan aborted before completion");
                }
            }
            catch (OperationCanceledException)
            {
                _state.Phase = _safety.IsEmergencyStopActive ? ScanPhase.EmergencyStop : ScanPhase.Idle;
                UpdateState();
                _log.Warning("SCAN", _safety.IsEmergencyStopActive
                    ? "Scan cancelled — EMERGENCY STOP active"
                    : "Scan cancelled by operator");
            }
            catch (Exception ex)
            {
                _state.Phase = ScanPhase.Error;
                UpdateState();
                _log.Error("SCAN", $"Scan error: {ex.Message}");
                _logger.LogError(ex, "Scan failed");
                ErrorOccurred?.Invoke(this, ex.Message);
            }
        }

        private async Task<double> ReadAveragedS21Async(
            ScanConfiguration config,
            double centerFreqHz,
            CancellationToken ct)
        {
            int n = Math.Max(1, config.SamplesPerPoint);
            double sum = 0;
            int valid = 0;

            for (int i = 0; i < n; i++)
            {
                if (_safety.IsEmergencyStopActive)
                    throw new OperationCanceledException("Emergency stop");

                await _hw.Vna.TriggerSweepAsync(ct);
                double v = await _hw.Vna.ReadSinglePointS21Async(centerFreqHz, ct);
                if (!double.IsNaN(v) && !double.IsInfinity(v))
                {
                    sum += v;
                    valid++;
                }

                if (i < n - 1)
                    await Task.Delay(30, ct);
            }

            return valid > 0 ? sum / valid : double.NaN;
        }

        private void UpdateState()
        {
            _state.LastUpdate = DateTime.UtcNow;
            StateUpdated?.Invoke(this, _state);
        }
    }
}
