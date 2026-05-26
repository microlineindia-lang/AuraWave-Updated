using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;

namespace AuraWave.Core.Services;

public sealed class HardwareManager : IHardwareManager
{
    private readonly IVnaController _vna;
    private readonly ITurntableController _turntable;
    private readonly IRfSwitchController _rfSwitch;

    public IVnaController Vna => _vna;
    public ITurntableController Turntable => _turntable;
    public IRfSwitchController RfSwitch => _rfSwitch;
    public SystemHardwareState State { get; private set; } = new();

    public event EventHandler<SystemHardwareState>? StateChanged;

    public HardwareManager(
        IVnaController vna,
        ITurntableController turntable,
        IRfSwitchController rfSwitch)
    {
        _vna = vna;
        _turntable = turntable;
        _rfSwitch = rfSwitch;

        _turntable.PositionChanged += (_, _) => _ = RefreshStatusAsync();
        _vna.SweepCompleted += (_, _) => _ = RefreshStatusAsync();
    }

    public Task RefreshStatusAsync()
    {
        State = new SystemHardwareState
        {
            Vna = _vna.Status,
            Turntable = _turntable.Status,
            RfSwitch = _rfSwitch.Status,
            Chamber = new ChamberEnvironment
            {
                DoorLocked = true,
                ShieldingIntact = true,
                TemperatureCelsius = 22.5,
                HumidityPercent = 45,
                LastUpdate = DateTime.UtcNow
            }
        };

        StateChanged?.Invoke(this, State);
        return Task.CompletedTask;
    }
}
