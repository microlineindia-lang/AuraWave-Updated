using System.Text.Json;
using System.Text.Json.Serialization;
using AuraWave.Core.Configuration;
using AuraWave.Core.Interfaces;

namespace AuraWave.Core.Services;

public sealed class SettingsPersistenceService : ISettingsPersistenceService
{
    private readonly ILogService _log;
    private readonly string _settingsPath;

    public SettingsPersistenceService(ILogService log)
    {
        _log = log;
        _settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    public async Task SaveAsync(
        HardwareConfiguration hardware,
        ApplicationSettings application,
        CancellationToken ct = default)
    {
        var root = new Dictionary<string, object?>
        {
            ["Hardware"] = new Dictionary<string, object?>
            {
                ["VnaType"] = hardware.VnaType.ToString(),
                ["VnaTcpHost"] = hardware.VnaTcpHost,
                ["VnaTcpPort"] = hardware.VnaTcpPort,
                ["VnaSerialPort"] = hardware.VnaSerialPort,
                ["VnaSerialBaud"] = hardware.VnaSerialBaud,
                ["TurntableType"] = hardware.TurntableType.ToString(),
                ["TurntablePort"] = hardware.TurntablePort,
                ["TurntableBaud"] = hardware.TurntableBaud,
                ["RfSwitchPort"] = hardware.RfSwitchPort,
                ["RfSwitchBaud"] = hardware.RfSwitchBaud,
                ["MotorStepSizeDegrees"] = hardware.MotorStepSizeDegrees,
                ["DefaultTurntableSpeedDegPerSec"] = hardware.DefaultTurntableSpeedDegPerSec
            },
            ["Application"] = new Dictionary<string, object?>
            {
                ["OperatorName"] = application.OperatorName,
                ["ChamberId"] = application.ChamberId,
                ["LogLevel"] = application.LogLevel,
                ["LogScpiToConsole"] = application.LogScpiToConsole,
                ["LogSerialToConsole"] = application.LogSerialToConsole,
                ["FarFieldMinDistanceM"] = application.FarFieldMinDistanceM,
                ["DefaultScanPlane"] = application.DefaultScanPlane.ToString()
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        string json = JsonSerializer.Serialize(root, options);
        await File.WriteAllTextAsync(_settingsPath, json, ct);
        _log.Info("SETTINGS", $"Configuration saved to {_settingsPath}");
    }
}
