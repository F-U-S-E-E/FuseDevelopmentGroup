using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using FUSE.Authoring.Editor;
using Xunit;

namespace FUSE.Tests.Authoring.Editor
{
    /// <summary>
    /// Locks the optional-editor contract: FuseEditorAssemblyLoader must
    /// never throw for missing/empty/invalid mod paths (FUSE.Editor.dll is
    /// optional at runtime), and it must invoke
    /// <c>FUSE.Editor.FuseEditorBootstrap.Initialize()</c> exactly once
    /// when a valid editor DLL is present. The happy-path test synthesizes
    /// a stub editor DLL via Reflection.Emit so the suite doesn't depend
    /// on FUSE.Editor.dll being built or copied into the test output.
    /// </summary>
    [Collection(FuseEditorBridgeTestCollection.Name)]
    public sealed class FuseEditorAssemblyLoaderTests : IDisposable
    {
        private const string SentinelEnvVar = "FUSE_EDITOR_BOOTSTRAP_SENTINEL";

        private readonly string _tempRoot;

        public FuseEditorAssemblyLoaderTests()
        {
            ResetInitializedFlag();
            Environment.SetEnvironmentVariable(SentinelEnvVar, null);
            _tempRoot = Path.Combine(Path.GetTempPath(), "FuseEditorAssemblyLoaderTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            ResetInitializedFlag();
            Environment.SetEnvironmentVariable(SentinelEnvVar, null);

            if (FuseEditorBridge.LifecycleProvider != null)
            {
                FuseEditorBridge.ClearLifecycleProvider(FuseEditorBridge.LifecycleProvider);
            }

            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
            }
            catch
            {
                // The synthesized DLL is held by the loaded AppDomain; on
                // Windows that locks the file. Best-effort cleanup is
                // acceptable here — temp folder lives under %TEMP% and
                // the OS will reclaim it.
            }

            GC.SuppressFinalize(this);
        }

        private static void ResetInitializedFlag()
        {
            var field = typeof(FuseEditorAssemblyLoader)
                .GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            field.SetValue(null, false);
        }

        [Fact]
        public void TryInitialize_null_path_returns_false()
        {
            Assert.False(FuseEditorAssemblyLoader.TryInitialize(null));
            Assert.Null(Environment.GetEnvironmentVariable(SentinelEnvVar));
        }

        [Fact]
        public void TryInitialize_empty_path_returns_false()
        {
            Assert.False(FuseEditorAssemblyLoader.TryInitialize(string.Empty));
            Assert.Null(Environment.GetEnvironmentVariable(SentinelEnvVar));
        }

        [Fact]
        public void TryInitialize_whitespace_path_returns_false()
        {
            Assert.False(FuseEditorAssemblyLoader.TryInitialize("   "));
            Assert.Null(Environment.GetEnvironmentVariable(SentinelEnvVar));
        }

        [Fact]
        public void TryInitialize_nonexistent_folder_returns_false()
        {
            var fakePath = Path.Combine(_tempRoot, "does-not-exist");

            Assert.False(FuseEditorAssemblyLoader.TryInitialize(fakePath));
            Assert.Null(Environment.GetEnvironmentVariable(SentinelEnvVar));
        }

        [Fact]
        public void TryInitialize_folder_without_editor_dll_returns_false()
        {
            // Folder exists (created in ctor) but contains no FUSE.Editor.dll.
            Assert.False(FuseEditorAssemblyLoader.TryInitialize(_tempRoot));
            Assert.Null(Environment.GetEnvironmentVariable(SentinelEnvVar));
        }

        [Fact]
        public void TryInitialize_with_valid_editor_dll_invokes_bootstrap()
        {
            EmitStubEditorAssembly(_tempRoot, sentinelValue: "loaded-ok");

            var result = FuseEditorAssemblyLoader.TryInitialize(_tempRoot);

            Assert.True(result);
            Assert.Equal("loaded-ok", Environment.GetEnvironmentVariable(SentinelEnvVar));
        }

        [Fact]
        public void TryInitialize_is_idempotent()
        {
            EmitStubEditorAssembly(_tempRoot, sentinelValue: "first");

            Assert.True(FuseEditorAssemblyLoader.TryInitialize(_tempRoot));
            Assert.Equal("first", Environment.GetEnvironmentVariable(SentinelEnvVar));

            // Replace the sentinel so we can detect whether Initialize ran
            // a second time. Idempotency means the loader short-circuits
            // and the sentinel stays untouched.
            Environment.SetEnvironmentVariable(SentinelEnvVar, "untouched");

            Assert.True(FuseEditorAssemblyLoader.TryInitialize(_tempRoot));
            Assert.Equal("untouched", Environment.GetEnvironmentVariable(SentinelEnvVar));
        }

        /// <summary>
        /// Synthesizes a <c>FUSE.Editor.dll</c> at <paramref name="targetDir"/>
        /// containing a public static <c>FUSE.Editor.FuseEditorBootstrap</c>
        /// type with an <c>Initialize()</c> method that writes
        /// <paramref name="sentinelValue"/> to the <see cref="SentinelEnvVar"/>
        /// environment variable. The assembly's internal identity name is
        /// unique per call so repeated invocations within the same
        /// AppDomain don't collide with previously-loaded synths.
        /// </summary>
        private static void EmitStubEditorAssembly(string targetDir, string sentinelValue)
        {
            // Unique internal name avoids the .NET assembly resolver
            // returning a previously-loaded dynamic assembly when the file
            // is loaded via Assembly.LoadFrom from a different temp dir.
            var assemblyName = new AssemblyName("FUSE.Editor.TestSynth." + Guid.NewGuid().ToString("N"));

            var asmBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(
                assemblyName,
                AssemblyBuilderAccess.RunAndSave,
                targetDir);

            // The on-disk file name is what FuseEditorAssemblyLoader looks
            // for (Path.Combine(modFolder, "FUSE.Editor.dll")). Keep the
            // module + save filename in sync so they end up as the same
            // physical file the loader probes for.
            const string FileName = "FUSE.Editor.dll";
            var moduleBuilder = asmBuilder.DefineDynamicModule(FileName, FileName);

            var typeBuilder = moduleBuilder.DefineType(
                "FUSE.Editor.FuseEditorBootstrap",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class);

            var methodBuilder = typeBuilder.DefineMethod(
                "Initialize",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                Type.EmptyTypes);

            var setEnvVar = typeof(Environment).GetMethod(
                nameof(Environment.SetEnvironmentVariable),
                new[] { typeof(string), typeof(string) });
            Assert.NotNull(setEnvVar);

            var il = methodBuilder.GetILGenerator();
            il.Emit(OpCodes.Ldstr, SentinelEnvVar);
            il.Emit(OpCodes.Ldstr, sentinelValue);
            il.Emit(OpCodes.Call, setEnvVar);
            il.Emit(OpCodes.Ret);

            typeBuilder.CreateType();
            asmBuilder.Save(FileName);
        }
    }
}
