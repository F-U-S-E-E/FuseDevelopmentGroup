using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Fuse.ExternalEditor.Services;
using Fuse.ExternalEditor.ViewModels;

namespace Fuse.ExternalEditor.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType FuseModType = new("FUSE mod")
    {
        Patterns = new[] { "*.fuse.json", "*.json" },
    };

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnOpenTerrainFolder(object? sender, RoutedEventArgs e)
    {
        var top = GetTopLevel(this);
        if (top is null || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open terrain tile folder",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            try
            {
                vm.Viewport.LoadFolder(folders[0].Path.LocalPath);
            }
            catch (Exception ex)
            {
                vm.Status = "Open terrain failed: " + ex.Message;
            }
        }
    }

    private async void OnOpenFuseMod(object? sender, RoutedEventArgs e)
    {
        var top = GetTopLevel(this);
        if (top is null || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open FUSE mod (*.fuse.json)",
            AllowMultiple = false,
            FileTypeFilter = new[] { FuseModType },
        });

        if (files.Count > 0)
        {
            try
            {
                vm.TrackGraph.OpenProject(files[0].Path.LocalPath);
            }
            catch (Exception ex)
            {
                vm.Status = "Open mod failed: " + ex.Message;
            }
        }
    }

    private async void OnSaveFuseMod(object? sender, RoutedEventArgs e)
    {
        var top = GetTopLevel(this);
        if (top is null || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save FUSE mod",
            SuggestedFileName = "mod.fuse.json",
            DefaultExtension = "fuse.json",
            FileTypeChoices = new[] { FuseModType },
        });

        if (file is not null)
        {
            try
            {
                vm.TrackGraph.Save(file.Path.LocalPath);
            }
            catch (Exception ex)
            {
                vm.Status = "Save failed: " + ex.Message;
            }
        }
    }

    private async void OnNewMod(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            try
            {
                await vm.NewModAsync(new AvaloniaDialogService(this));
            }
            catch (Exception ex)
            {
                vm.Status = "New mod failed: " + ex.Message;
            }
        }
    }

    private async void OnConvertLegacyMod(object? sender, RoutedEventArgs e)
    {
        var top = GetTopLevel(this);
        if (top is null || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a legacy (Strange Customs / Railloader) mod folder to convert",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            try
            {
                vm.ImportLegacyMod(folders[0].Path.LocalPath);
            }
            catch (Exception ex)
            {
                vm.Status = "Legacy import failed: " + ex.Message;
            }
        }
    }

    private async void OnSetGenOutputFolder(object? sender, RoutedEventArgs e)
    {
        var top = GetTopLevel(this);
        if (top is null || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Output folder for generated terrain tiles",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            vm.Generation.OutputFolder = folders[0].Path.LocalPath;
        }
    }

    private async void OnSetModsFolder(object? sender, RoutedEventArgs e)
    {
        var top = GetTopLevel(this);
        if (top is null || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the running game's Mods folder",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            vm.TrackGraph.GameModsPath = folders[0].Path.LocalPath;
            vm.TrackGraph.RefreshBridgeStatusCommand.Execute(null);
        }
    }
}
