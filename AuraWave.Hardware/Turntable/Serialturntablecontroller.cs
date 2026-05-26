using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;
using Microsoft.Extensions.Logging;

namespace AuraWave.Hardware.Turntable
{
    /// <summary>
    /// Serial-protocol turntable controller.
    /// Protocol: ASCII commands terminated with CR+LF.
    /// </summary>
    public sealed class SerialTurntableController : ITurntableController
    {
        private readonly ILogger<SerialTurntableController> _logger;
        private readonly ILogService _logService;
        private SerialPort? _port;
        private readonly TurntableStatus _status = new();
        private readonly SemaphoreSlim _comLock = new(1, 1);
        private bool _disposed;

        public bool IsConnected => _status.IsConnected;
        public bool IsHomed => _status.IsHomed && !_status.EmergencyStop;
        public double CurrentAngle => _status.CurrentAngleDeg;
        public TurntableStatus Status => _status;

        public event EventHandler<double>? PositionChanged;
        public event EventHandler? HomeComplete;
        public event EventHandler<string>? SerialMessageSent;
        public event EventHandler<string>? SerialResponseReceived;

        public SerialTurntableController(
            ILogger<SerialTurntableController> logger,
            ILogService logService)
        {
            _logger = logger;
            _logService = logService;
        }

        public async Task<bool> ConnectAsync(string portName, int baudRate = 115200, CancellationToken ct = default)
        {
            try
            {
                _logService.Info("TURNTABLE", $"Connecting to {portName} @ {baudRate}...");
                await Task.Run(() =>
                {
                    _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = 3000,
                        WriteTimeout = 3000,
                        NewLine = "\r\n"
                    };
                    _port.Open();
                }, ct);

                var resp = await SendCommandAsync("IDENT", ct);
                _status.IsConnected = true;
                _status.PortName = portName;
                _logService.Info("TURNTABLE", $"Connected: {resp}");

                _ = PollPositionAsync(ct);
                return true;
            }
            catch (Exception ex)
            {
                _status.ErrorMessage = ex.Message;
                _logService.Error("TURNTABLE", $"Connect failed: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_port?.IsOpen == true)
            {
                await Task.Run(() => _port.Close());
                _status.IsConnected = false;
                _logService.Info("TURNTABLE", "Disconnected");
            }
        }

        public async Task HomeAsync(CancellationToken ct = default)
        {
            EnsureNotEstopLocked();
            _logService.Info("TURNTABLE", "Homing — clockwise then anticlockwise to reference...");
            _status.IsMoving = true;

            await SendCommandAsync("HOME", ct);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            while (!linked.Token.IsCancellationRequested)
            {
                var resp = await ReadLineAsync(linked.Token);

                if (resp.StartsWith("OK:HOME_CW", StringComparison.OrdinalIgnoreCase))
                    _logService.Info("TURNTABLE", "Home phase 1: rotating clockwise");
                else if (resp.StartsWith("OK:HOME_CCW", StringComparison.OrdinalIgnoreCase))
                    _logService.Info("TURNTABLE", "Home phase 2: rotating anticlockwise");
                else if (resp.StartsWith("ERR:ESTOP", StringComparison.OrdinalIgnoreCase))
                {
                    _status.IsMoving = false;
                    _status.EmergencyStop = true;
                    throw new InvalidOperationException("Homing aborted by emergency stop.");
                }
                else if (resp.StartsWith("OK:HOMED", StringComparison.OrdinalIgnoreCase))
                {
                    _status.IsHomed = true;
                    _status.IsMoving = false;
                    _status.CurrentAngleDeg = 0;
                    _logService.Info("TURNTABLE", "Calibration complete — reference 0°");
                    HomeComplete?.Invoke(this, EventArgs.Empty);
                    PositionChanged?.Invoke(this, 0);
                    return;
                }
                else if (resp.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase))
                {
                    _status.IsMoving = false;
                    throw new InvalidOperationException($"Home error: {resp}");
                }
            }

            _status.IsMoving = false;
            throw new TimeoutException("Turntable homing timed out.");
        }

        public async Task MoveToAngleAsync(double angleDeg, CancellationToken ct = default)
        {
            EnsureNotEstopLocked();
            _status.IsMoving = true;
            _status.TargetAngleDeg = angleDeg;
            _logService.Debug("TURNTABLE", $"MoveTo {angleDeg:F3}°");
            await SendCommandAsync($"MOVETO:{angleDeg:F3}", ct);
            await WaitForMotionCompleteAsync(ct);
        }

        public async Task MoveRelativeAsync(double deltaDeg, CancellationToken ct = default)
        {
            EnsureNotEstopLocked();
            _status.IsMoving = true;
            _logService.Debug("TURNTABLE", $"MoveRel {deltaDeg:F3}°");
            await SendCommandAsync($"MOVEREL:{deltaDeg:F3}", ct);
            await WaitForMotionCompleteAsync(ct);
        }

        public Task SetSpeedAsync(double degPerSec, CancellationToken ct = default)
        {
            EnsureNotEstopLocked();
            _status.SpeedDegPerSec = degPerSec;
            return SendCommandAsync($"SPEED:{degPerSec:F2}", ct);
        }

        public async Task StopAsync()
        {
            if (_port?.IsOpen != true) return;
            try
            {
                await SendCommandAsync("STOP");
            }
            catch { /* ignore */ }
            _status.IsMoving = false;
        }

        public async Task EmergencyStopAsync()
        {
            _status.EmergencyStop = true;
            _status.IsMoving = false;
            _status.IsHomed = false;

            if (_port?.IsOpen != true)
            {
                _logService.Warning("TURNTABLE", "E-STOP latched (turntable not connected)");
                return;
            }

            _logService.Warning("TURNTABLE", "EMERGENCY STOP — immediate motor halt");

            if (await _comLock.WaitAsync(TimeSpan.FromMilliseconds(250)))
            {
                try
                {
                    FireEstopBytes();
                }
                finally
                {
                    _comLock.Release();
                }
            }
            else
            {
                FireEstopBytes();
            }

            try
            {
                var line = await ReadLineAsync(CancellationToken.None);
                _logService.Scpi("TTL<<", line);
            }
            catch { /* port may be busy */ }
        }

        public async Task ClearEmergencyStopAsync(CancellationToken ct = default)
        {
            if (_port?.IsOpen == true)
            {
                await SendCommandAsync("CLR_ESTOP", ct);
            }
            _status.EmergencyStop = false;
            _logService.Info("TURNTABLE", "Emergency stop cleared on controller");
        }

        private void FireEstopBytes()
        {
            const string cmd = "ESTOP\r\n";
            SerialMessageSent?.Invoke(this, "ESTOP");
            _logService.Scpi("TTL>>", "ESTOP");
            _port!.DiscardInBuffer();
            _port.Write(cmd);
            _port.BaseStream.Flush();
        }

        private void EnsureNotEstopLocked()
        {
            if (_status.EmergencyStop)
                throw new InvalidOperationException(
                    "Turntable emergency stop is active. Clear E-STOP before motion commands.");
        }

        private async Task WaitForMotionCompleteAsync(CancellationToken ct)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            while (!linked.Token.IsCancellationRequested)
            {
                var line = await ReadLineAsync(linked.Token);
                if (line.StartsWith("POS:", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(line[4..], out double pos))
                    {
                        _status.CurrentAngleDeg = pos;
                        PositionChanged?.Invoke(this, pos);
                    }
                }
                if (line.StartsWith("OK:DONE", StringComparison.OrdinalIgnoreCase))
                {
                    _status.IsMoving = false;
                    return;
                }
                if (line.StartsWith("ERR:ESTOP", StringComparison.OrdinalIgnoreCase))
                {
                    _status.EmergencyStop = true;
                    _status.IsMoving = false;
                    throw new InvalidOperationException("Motion aborted by emergency stop.");
                }
            }
        }

        private async Task PollPositionAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _status.IsConnected)
            {
                try
                {
                    await Task.Delay(500, ct);
                    if (!_status.IsMoving && !_status.EmergencyStop)
                    {
                        var resp = await SendCommandAsync("POS?", ct);
                        if (resp.StartsWith("POS:", StringComparison.OrdinalIgnoreCase) &&
                            double.TryParse(resp[4..], out double angle))
                        {
                            _status.CurrentAngleDeg = angle;
                            _status.LastUpdate = DateTime.UtcNow;
                            PositionChanged?.Invoke(this, angle);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* ignore transient poll errors */ }
            }
        }

        private async Task<string> SendCommandAsync(string command, CancellationToken ct = default)
        {
            await _comLock.WaitAsync(ct);
            try
            {
                SerialMessageSent?.Invoke(this, command);
                _logService.Scpi("TTL>>", command);
                await Task.Run(() => _port!.WriteLine(command), ct);
                var response = await ReadLineAsync(ct);
                SerialResponseReceived?.Invoke(this, response);
                _logService.Scpi("TTL<<", response);
                return response;
            }
            finally
            {
                _comLock.Release();
            }
        }

        private Task<string> ReadLineAsync(CancellationToken ct)
            => Task.Run(() => _port!.ReadLine(), ct);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _comLock.Dispose();
            _port?.Dispose();
        }
    }
}
