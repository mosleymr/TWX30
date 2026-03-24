using Microsoft.Extensions.Logging;

namespace TWXP;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register services
        builder.Services.AddSingleton<IGameConfigService, GameConfigService>();
        builder.Services.AddSingleton<IProxyService, ProxyService>();
        builder.Services.AddSingleton<IDirectoryPickerService, DirectoryPickerService>();

        // Register view models
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<GameConfigViewModel>();

        // Register pages
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<GameConfigPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
