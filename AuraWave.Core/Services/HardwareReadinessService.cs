using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;

namespace AuraWave.Core.Services;

public sealed class HardwareReadinessService : IHardwareReadinessService
{
    private readonly ISafetyService _safety;

    public HardwareReadinessService(ISafetyService safety) => _safety = safety;

    public bool IsReadyForScan(SystemHardwareState state, bool requireRfSwitch, out string blockingReason)
    {
        if (_safety.IsEmergencyStopActive)
        {
            blockingReason = "Emergency stop is ACTIVE. Clear E-STOP on Hardware Control or the sidebar before starting.";
            return false;
        }

        if (!state.Vna.IsConnected)
        {
            blockingReason = "VNA is not connected. Open Hardware Control and connect the vector network analyzer (TCP or serial SCPI).";
            return false;
        }

        if (!state.Turntable.IsConnected)
        {
            blockingReason = "Turntable is not connected. Connect the Arduino/stepper controller on the configured COM port.";
            return false;
        }

        if (!state.Turntable.IsHomed)
        {
            blockingReason = "Turntable is not homed. Run HOME on Hardware Control before starting a pattern scan.";
            return false;
        }

        if (requireRfSwitch && !state.RfSwitch.IsConnected)
        {
            blockingReason = "RF switch is not connected. Connect the polarization switch or disable dual-plane requirement in setup.";
            return false;
        }

        blockingReason = string.Empty;
        return true;
    }
}
