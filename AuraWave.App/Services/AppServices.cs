using Microsoft.Extensions.DependencyInjection;

namespace AuraWave.App.Services;

public static class AppServices
{
    public static IServiceProvider Provider { get; private set; } = null!;

    public static void Initialize(IServiceProvider provider) => Provider = provider;

    public static T GetRequired<T>() where T : notnull => Provider.GetRequiredService<T>();
}
