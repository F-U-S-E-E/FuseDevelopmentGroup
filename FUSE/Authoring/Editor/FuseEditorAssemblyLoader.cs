using System;
using System.IO;
using System.Reflection;
using FUSE.Infrastructure;

namespace FUSE.Authoring.Editor
{
    /// <summary>
    /// Loads FUSE.Editor.dll lazily from the mod folder and invokes its
    /// FUSE.Editor.FuseEditorBootstrap.Initialize() entry point. The
    /// bootstrap then registers an IFuseEditorLifecycle implementation
    /// with FuseEditorBridge; from that point on FUSE talks to the editor
    /// through typed bridge interfaces and no further reflection is used.
    /// </summary>
    internal static class FuseEditorAssemblyLoader
    {
        private const string EditorAssemblyFileName = "FUSE.Editor.dll";
        private const string BootstrapTypeName = "FUSE.Editor.FuseEditorBootstrap";
        private const string BootstrapInitializeMethod = "Initialize";

        private static bool _initialized;

        public static bool TryInitialize(string modFolderPath)
        {
            if (_initialized)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(modFolderPath))
            {
                FuseLog.Info("FUSE.Editor bootstrap skipped: mod folder path is empty.");
                return false;
            }

            var editorDllPath = Path.Combine(modFolderPath, EditorAssemblyFileName);
            if (!File.Exists(editorDllPath))
            {
                FuseLog.Info($"FUSE.Editor bootstrap skipped: '{editorDllPath}' was not found alongside FUSE.dll.");
                return false;
            }

            try
            {
                var assembly = Assembly.LoadFrom(editorDllPath);
                var bootstrapType = assembly.GetType(BootstrapTypeName, throwOnError: false, ignoreCase: false);
                if (bootstrapType == null)
                {
                    FuseLog.Warning($"FUSE.Editor bootstrap skipped: type '{BootstrapTypeName}' not found in '{EditorAssemblyFileName}'.");
                    return false;
                }

                var initMethod = bootstrapType.GetMethod(
                    BootstrapInitializeMethod,
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
                if (initMethod == null)
                {
                    FuseLog.Warning($"FUSE.Editor bootstrap skipped: static method '{BootstrapInitializeMethod}()' not found on '{BootstrapTypeName}'.");
                    return false;
                }

                initMethod.Invoke(null, null);
                _initialized = true;
                FuseLog.Info("FUSE.Editor bootstrap completed; lifecycle provider registered via FuseEditorBridge.");
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE.Editor bootstrap failed while loading '{editorDllPath}'", ex);
                return false;
            }
        }
    }
}
