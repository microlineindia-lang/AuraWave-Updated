using AuraWave.Core.Configuration;
using AuraWave.Core.Interfaces;
using AuraWave.Hardware.RfSwitch;
using AuraWave.Hardware.Turntable;
using AuraWave.Hardware.VNA;
using Microsoft.Extensions.DependencyInjection;

namespace AuraWave.Hardware.DependencyInjection;

public static class HardwareServiceExtensions
{
    /// <summary>Registers physical instrument drivers only (serial turntable, SCPI VNA, serial RF switch).</summary>
    public static IServiceCollection AddAuraWaveHardware(this IServiceCollection services)
    {
        services.AddSingleton<ScpiVnaController>();
        services.AddSingleton<SerialTurntableController>();
        services.AddSingleton<SerialRfSwitchController>();

        services.AddSingleton<IVnaController>(sp => sp.GetRequiredService<ScpiVnaController>());
        services.AddSingleton<ITurntableController>(sp => sp.GetRequiredService<SerialTurntableController>());
        services.AddSingleton<IRfSwitchController>(sp => sp.GetRequiredService<SerialRfSwitchController>());

        return services;
    }
}
