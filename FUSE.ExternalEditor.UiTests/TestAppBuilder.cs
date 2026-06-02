using Avalonia;
using Avalonia.Headless;
using Fuse.ExternalEditor;
using Fuse.ExternalEditor.UiTests;

// Registers the headless Avalonia application used by every [AvaloniaFact]/
// [AvaloniaTheory] in this assembly. Reuses the real App (App.axaml + styles)
// so tests exercise the same control templates and resources the app ships.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Fuse.ExternalEditor.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
