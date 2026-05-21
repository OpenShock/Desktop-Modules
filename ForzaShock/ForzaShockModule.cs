using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenShock.Desktop.ModuleBase;
using OpenShock.Desktop.ModuleBase.Models;
using OpenShock.Desktop.ModuleBase.Navigation;
using OpenShock.Desktop.Modules.ForzaShock;

[assembly: DesktopModule(typeof(ForzaShockModule), "openshock.desktop.modules.forzashock", "ForzaShock")]
[assembly: RequiredPermission(TokenPermissions.Shockers_Use)]

namespace OpenShock.Desktop.Modules.ForzaShock;

public sealed class ForzaShockModule : DesktopModuleBase
{
    public override IconOneOf? Icon { get; set; } =
        IconOneOf.FromPath("OpenShock/Desktop/Modules/ForzaShock/Icon.svg");

    public override Type RootComponent { get; } = typeof(Components.ForzaShockUi);

    public override IReadOnlyCollection<NavigationItem> NavigationComponents { get; } = [];

    public override async Task Start()
    {
        var config = await ModuleInstanceManager.GetModuleConfig<ForzaShockModuleConfig>();

        var loggerFactory = ModuleInstanceManager.AppServiceProvider.GetRequiredService<ILoggerFactory>();
        
        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddLogging();
        services.AddSingleton(config);
        services.AddSingleton(ModuleInstanceManager.OpenShock.Control);
        services.AddSingleton(ModuleInstanceManager.OpenShock);
        services.AddSingleton<TelemetryListener>();

        ModuleServiceProvider = services.BuildServiceProvider();

        if (config.Config.AutoStart)
            ModuleServiceProvider.GetRequiredService<TelemetryListener>().Start();
    }
}
