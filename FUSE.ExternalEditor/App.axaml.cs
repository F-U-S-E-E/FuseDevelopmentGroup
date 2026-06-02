using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.Net.Http;
using Avalonia.Markup.Xaml;
using Fuse.Core.Authoring;
using Fuse.ExternalEditor.Services;
using Fuse.ExternalEditor.ViewModels;
using Fuse.ExternalEditor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fuse.ExternalEditor;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Service registration is shared between the running app and the headless
    /// UI tests so both compose the same object graph.
    /// </summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<ILiveBridgeService, LiveBridgeService>();
        services.AddSingleton<ITerrainTileService, TerrainTileService>();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ITerrainGenerationService, TerrainGenerationService>();
        services.AddSingleton<IOsmTileService, OsmTileService>();
        services.AddSingleton<UndoService>();
        services.AddSingleton<ViewportViewModel>();
        services.AddSingleton<TrackGraphViewModel>();
        services.AddSingleton<TerrainEditViewModel>();
        services.AddSingleton<EntityTreeViewModel>();
        services.AddSingleton<ILegacyImportService, LegacyImportService>();
        services.AddSingleton<GenerationViewModel>();
        services.AddSingleton<OsmOverlayViewModel>();
        services.AddSingleton<ProfileViewModel>();
        services.AddTransient<MainWindowViewModel>();
    }
}
