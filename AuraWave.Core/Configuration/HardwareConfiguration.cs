namespace AuraWave.Core.Configuration;

public enum VnaConnectionType
{
    TcpScpi,
    SerialScpi
}

public enum TurntableConnectionType
{
    SerialArduino
}

public enum ScanPlaneType
{
    /// <summary>Horizontal plane — azimuth sweep (0–360°).</summary>
    Azimuth,

    /// <summary>Vertical plane — elevation sweep.</summary>
    Elevation
}

public sealed class HardwareConfiguration
{
    public VnaConnectionType VnaType { get; set; } = VnaConnectionType.TcpScpi;
    public string VnaTcpHost { get; set; } = "192.168.1.100";
    public int VnaTcpPort { get; set; } = 5025;
    public string VnaSerialPort { get; set; } = "COM5";
    public int VnaSerialBaud { get; set; } = 115200;

    public TurntableConnectionType TurntableType { get; set; } = TurntableConnectionType.SerialArduino;
    public string TurntablePort { get; set; } = "COM3";
    public int TurntableBaud { get; set; } = 115200;

    public string RfSwitchPort { get; set; } = "COM4";
    public int RfSwitchBaud { get; set; } = 9600;

    /// <summary>NEMA 17 0.9°/step → 400 steps/revolution.</summary>
    public double MotorStepSizeDegrees { get; set; } = 0.9;

    public double DefaultTurntableSpeedDegPerSec { get; set; } = 5.0;

    public string GetVnaConnectionString() => VnaType switch
    {
        VnaConnectionType.SerialScpi => $"{VnaSerialPort}@{VnaSerialBaud}",
        _ => $"{VnaTcpHost}:{VnaTcpPort}"
    };
}

public sealed class ApplicationSettings
{
    public string OperatorName { get; set; } = "Lab User";
    public string ChamberId { get; set; } = "Chamber-01";
    public string LogLevel { get; set; } = "Debug";
    public bool LogScpiToConsole { get; set; } = true;
    public bool LogSerialToConsole { get; set; } = true;
    public double FarFieldMinDistanceM { get; set; } = 1.0;
    public ScanPlaneType DefaultScanPlane { get; set; } = ScanPlaneType.Azimuth;
}
