using System.Globalization;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;
using Microsoft.Extensions.Logging;

namespace AuraWave.Hardware.VNA;

/// <summary>SCPI VNA over TCP (VISA SOCKET) or serial port.</summary>
public sealed class ScpiVnaController : IVnaController, IDisposable
{
    private readonly ILogService _log;
    private readonly ILogger<ScpiVnaController> _logger;
    private readonly VnaStatus _status = new();
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private SerialPort? _serial;
    private bool _useTcp = true;

    private double _startFreqHz = 1e9;
    private double _stopFreqHz = 6e9;
    private int _sweepPoints = 401;
    private double _ifBwHz = 10e3;
    private double _powerDbm = -10;

    public string InstrumentId { get; private set; } = string.Empty;
    public bool IsConnected => _status.IsConnected;
    public VnaStatus Status => _status;

    public event EventHandler<SParameterData>? SweepCompleted;
    public event EventHandler<string>? ScpiMessageSent;
    public event EventHandler<string>? ScpiResponseReceived;

    public ScpiVnaController(ILogService log, ILogger<ScpiVnaController> logger)
    {
        _log = log;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(string resourceAddress, CancellationToken ct = default)
    {
        try
        {
            DisconnectInternal();

            if (resourceAddress.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                _useTcp = false;
                var parts = resourceAddress.Split('@');
                var port = parts[0];
                var baud = parts.Length > 1 && int.TryParse(parts[1], out int b) ? b : 115200;
                await Task.Run(() =>
                {
                    _serial = new SerialPort(port, baud) { ReadTimeout = 15000, WriteTimeout = 5000, NewLine = "\n" };
                    _serial.Open();
                }, ct);
                _status.ResourceAddress = port;
            }
            else
            {
                _useTcp = true;
                var (host, port) = ParseTcpAddress(resourceAddress);
                _tcp = new TcpClient();
                await _tcp.ConnectAsync(host, port, ct);
                _stream = _tcp.GetStream();
                _status.ResourceAddress = $"{host}:{port}";
            }

            InstrumentId = (await QueryAsync("*IDN?", ct)).Trim();
            _status.InstrumentId = InstrumentId;
            _status.IsConnected = true;
            await SendAsync("*CLS", ct);
            await SendAsync("*RST", ct);
            _log.Info("VNA", $"Connected: {InstrumentId}");
            return true;
        }
        catch (Exception ex)
        {
            _status.ErrorMessage = ex.Message;
            _log.Error("VNA", $"Connect failed: {ex.Message}");
            return false;
        }
    }

    public Task DisconnectAsync()
    {
        DisconnectInternal();
        _log.Info("VNA", "Disconnected");
        return Task.CompletedTask;
    }

    public Task ResetAsync() => SendAsync("*RST");

    public Task SetStartFrequencyAsync(double freqHz, CancellationToken ct = default)
    {
        _startFreqHz = freqHz;
        return SendAsync($"SENS:FREQ:STAR {freqHz:E6}", ct);
    }

    public Task SetStopFrequencyAsync(double freqHz, CancellationToken ct = default)
    {
        _stopFreqHz = freqHz;
        return SendAsync($"SENS:FREQ:STOP {freqHz:E6}", ct);
    }

    public Task SetSweepPointsAsync(int points, CancellationToken ct = default)
    {
        _sweepPoints = points;
        return SendAsync($"SENS:SWE:POIN {points}", ct);
    }

    public Task SetIfBandwidthAsync(double bwHz, CancellationToken ct = default)
    {
        _ifBwHz = bwHz;
        return SendAsync($"SENS:BAND {bwHz:E3}", ct);
    }

    public Task SetPortPowerAsync(int port, double powerDbm, CancellationToken ct = default)
    {
        _powerDbm = powerDbm;
        return SendAsync($"SOUR{port}:POW {powerDbm:F2}", ct);
    }

    public async Task TriggerSweepAsync(CancellationToken ct = default)
    {
        _status.IsSweeping = true;
        await SendAsync("INIT:IMM", ct);
        await QueryAsync("*OPC?", ct);
        _status.IsSweeping = false;
    }

    public async Task AbortMeasurementAsync(CancellationToken ct = default)
    {
        if (!_status.IsConnected) return;

        try
        {
            _status.IsSweeping = false;
            await SendAsync(":ABOR", ct);
            _log.Warning("VNA", "Measurement aborted (:ABOR)");
        }
        catch (Exception ex)
        {
            _log.Error("VNA", $"Abort failed: {ex.Message}");
        }
    }

    public async Task<SParameterData> ReadS21Async(CancellationToken ct = default)
    {
        var data = await ReadTraceAsync("S21", ct);
        SweepCompleted?.Invoke(this, data);
        return data;
    }

    public async Task<SParameterData> ReadS11Async(CancellationToken ct = default)
    {
        return await ReadTraceAsync("S11", ct);
    }

    public async Task<double> ReadSinglePointS21Async(double freqHz, CancellationToken ct = default)
    {
        await SendAsync($"SENS:FREQ:CENT {freqHz:E6}", ct);
        await SendAsync("CALC:MARK1 ON", ct);
        await SendAsync("CALC:MARK1:SET", ct);
        var mag = await QueryAsync("CALC:MARK1:Y?", ct);
        if (double.TryParse(mag.Split(',')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double db))
            return db;
        return double.NaN;
    }

    public Task<string> SendRawScpiAsync(string command, CancellationToken ct = default)
        => command.TrimEnd().EndsWith('?') ? QueryAsync(command, ct) : SendAsync(command, ct).ContinueWith(_ => string.Empty, ct);

    private async Task<SParameterData> ReadTraceAsync(string parameter, CancellationToken ct)
    {
        await SendAsync($"CALC:PAR:DEF '{parameter}'", ct);
        await SendAsync("FORM:DATA ASC", ct);
        var raw = await QueryAsync("CALC:DATA? FDAT", ct);
        var values = ParseAsciiTrace(raw);
        var freqs = BuildFrequencyAxis();

        return new SParameterData
        {
            Parameter = parameter,
            Frequencies = freqs,
            MagnitudeDb = values.mag,
            PhaseDeg = values.phase,
            Timestamp = DateTime.UtcNow
        };
    }

    private double[] BuildFrequencyAxis()
    {
        if (_sweepPoints <= 1) return [_startFreqHz];
        var freqs = new double[_sweepPoints];
        for (int i = 0; i < _sweepPoints; i++)
            freqs[i] = _startFreqHz + (_stopFreqHz - _startFreqHz) * i / (_sweepPoints - 1);
        return freqs;
    }

    private static (double[] mag, double[] phase) ParseAsciiTrace(string raw)
    {
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int n = parts.Length / 2;
        if (n == 0) n = parts.Length;
        var mag = new double[n];
        var phase = new double[n];
        for (int i = 0; i < n && i < parts.Length; i++)
        {
            double.TryParse(parts[i * 2], NumberStyles.Float, CultureInfo.InvariantCulture, out mag[i]);
            if (i * 2 + 1 < parts.Length)
                double.TryParse(parts[i * 2 + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out phase[i]);
        }
        return (mag, phase);
    }

    private static (string host, int port) ParseTcpAddress(string address)
    {
        if (address.Contains("::", StringComparison.Ordinal))
        {
            var segments = address.Split("::", StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 3 && int.TryParse(segments[2], out int p))
                return (segments[1], p);
        }
        if (address.Contains(':'))
        {
            var parts = address.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[^1], out int port))
                return (parts[^2], port);
        }
        return (address, 5025);
    }

    private async Task SendAsync(string cmd, CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct);
        try
        {
            Emit(cmd);
            var bytes = Encoding.ASCII.GetBytes(cmd.TrimEnd() + "\n");
            if (_useTcp)
                await _stream!.WriteAsync(bytes, ct);
            else
                await Task.Run(() => _serial!.Write(cmd.TrimEnd() + "\n"), ct);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task<string> QueryAsync(string cmd, CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct);
        try
        {
            Emit(cmd);
            var line = _useTcp ? await ReadTcpLineAsync(ct) : await Task.Run(() => _serial!.ReadLine(), ct);
            Receive(line ?? string.Empty);
            return line ?? string.Empty;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task<string?> ReadTcpLineAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (!ct.IsCancellationRequested)
        {
            int n = await _stream!.ReadAsync(buf.AsMemory(0, 1), ct);
            if (n == 0) break;
            char c = (char)buf[0];
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
        }
        return sb.ToString();
    }

    private void Emit(string cmd)
    {
        ScpiMessageSent?.Invoke(this, cmd);
        _log.Scpi("VNA>>", cmd);
    }

    private void Receive(string resp)
    {
        ScpiResponseReceived?.Invoke(this, resp);
        _log.Scpi("VNA<<", resp);
    }

    private void DisconnectInternal()
    {
        _status.IsConnected = false;
        _stream?.Dispose();
        _tcp?.Dispose();
        _serial?.Dispose();
        _stream = null;
        _tcp = null;
        _serial = null;
    }

    public void Dispose()
    {
        DisconnectInternal();
        _ioLock.Dispose();
    }
}
