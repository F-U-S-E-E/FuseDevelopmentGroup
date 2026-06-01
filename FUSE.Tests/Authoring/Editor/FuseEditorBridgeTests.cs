using System;
using System.Reflection;
using FUSE.Authoring.Editor;
using FUSE.Authoring.Validation;
using Xunit;

namespace FUSE.Tests.Authoring.Editor
{
    /// <summary>
    /// Locks the FuseEditorBridge contract that lets FUSE.dll talk to
    /// FUSE.Editor.dll through typed interfaces without taking a hard
    /// project reference. Register/clear must be reference-equality based
    /// so a stale providers slot can't be cleared by an unrelated caller,
    /// and the Notify* methods must short-circuit when no provider is
    /// registered (the editor DLL is optional at runtime).
    /// </summary>
    [Collection(FuseEditorBridgeTestCollection.Name)]
    public sealed class FuseEditorBridgeTests : IDisposable
    {
        public FuseEditorBridgeTests()
        {
            ResetBridge();
        }

        public void Dispose()
        {
            ResetBridge();
            GC.SuppressFinalize(this);
        }

        private static void ResetBridge()
        {
            // Capture-then-clear each slot. Direct null-assignment via the
            // setter isn't exposed; ClearXxx with a matching reference is.
            if (FuseEditorBridge.LifecycleProvider != null)
            {
                FuseEditorBridge.ClearLifecycleProvider(FuseEditorBridge.LifecycleProvider);
            }
            if (FuseEditorBridge.EditorProvider != null)
            {
                FuseEditorBridge.ClearEditorProvider(FuseEditorBridge.EditorProvider);
            }
            FuseEditorBridge.SelectionProvider = null;
            FuseEditorBridge.IsEditorActive = false;

            ClearEditorExitedSubscribers();
        }

        /// <summary>
        /// EditorExited is a static event; subscribers leak across tests
        /// unless we reset the backing delegate field. Drop it via
        /// reflection so each test starts with no listeners.
        /// </summary>
        private static void ClearEditorExitedSubscribers()
        {
            var field = typeof(FuseEditorBridge)
                .GetField(nameof(FuseEditorBridge.EditorExited), BindingFlags.NonPublic | BindingFlags.Static);
            field?.SetValue(null, null);
        }

        [Fact]
        public void Default_state_has_no_providers()
        {
            Assert.Null(FuseEditorBridge.LifecycleProvider);
            Assert.Null(FuseEditorBridge.EditorProvider);
            Assert.Null(FuseEditorBridge.SelectionProvider);
            Assert.False(FuseEditorBridge.IsEditorActive);
        }

        [Fact]
        public void RegisterLifecycleProvider_sets_provider()
        {
            var provider = new FakeLifecycle();

            FuseEditorBridge.RegisterLifecycleProvider(provider);

            Assert.Same(provider, FuseEditorBridge.LifecycleProvider);
        }

        [Fact]
        public void RegisterLifecycleProvider_replaces_previous_provider()
        {
            var first = new FakeLifecycle();
            var second = new FakeLifecycle();

            FuseEditorBridge.RegisterLifecycleProvider(first);
            FuseEditorBridge.RegisterLifecycleProvider(second);

            Assert.Same(second, FuseEditorBridge.LifecycleProvider);
        }

        [Fact]
        public void ClearLifecycleProvider_matching_reference_nulls_slot()
        {
            var provider = new FakeLifecycle();
            FuseEditorBridge.RegisterLifecycleProvider(provider);

            FuseEditorBridge.ClearLifecycleProvider(provider);

            Assert.Null(FuseEditorBridge.LifecycleProvider);
        }

        [Fact]
        public void ClearLifecycleProvider_non_matching_reference_is_noop()
        {
            var registered = new FakeLifecycle();
            var other = new FakeLifecycle();
            FuseEditorBridge.RegisterLifecycleProvider(registered);

            FuseEditorBridge.ClearLifecycleProvider(other);

            Assert.Same(registered, FuseEditorBridge.LifecycleProvider);
        }

        [Fact]
        public void NotifyFuseLoaded_dispatches_to_provider()
        {
            var provider = new FakeLifecycle();
            FuseEditorBridge.RegisterLifecycleProvider(provider);

            FuseEditorBridge.NotifyFuseLoaded();

            Assert.Equal(1, provider.LoadedCount);
            Assert.Equal(0, provider.UnloadedCount);
        }

        [Fact]
        public void NotifyFuseUnloaded_dispatches_to_provider()
        {
            var provider = new FakeLifecycle();
            FuseEditorBridge.RegisterLifecycleProvider(provider);

            FuseEditorBridge.NotifyFuseUnloaded();

            Assert.Equal(0, provider.LoadedCount);
            Assert.Equal(1, provider.UnloadedCount);
        }

        [Fact]
        public void NotifyFuseLoaded_without_provider_does_not_throw()
        {
            // Default-state assumption: no provider registered. The editor
            // DLL is optional; FUSE must boot fine when it is missing.
            FuseEditorBridge.NotifyFuseLoaded();
        }

        [Fact]
        public void NotifyFuseUnloaded_without_provider_does_not_throw()
        {
            FuseEditorBridge.NotifyFuseUnloaded();
        }

        [Fact]
        public void NotifyEnterEditor_dispatches_to_provider()
        {
            var provider = new FakeLifecycle();
            FuseEditorBridge.RegisterLifecycleProvider(provider);

            FuseEditorBridge.NotifyEnterEditor();

            Assert.Equal(1, provider.EnteredCount);
            Assert.Equal(0, provider.LoadedCount);
            Assert.Equal(0, provider.UnloadedCount);
        }

        [Fact]
        public void NotifyEnterEditor_without_provider_does_not_throw()
        {
            // The main-menu patch already guards on
            // LifecycleProvider != null before showing the button, but the
            // bridge itself must still no-op if some other caller invokes
            // NotifyEnterEditor when the editor DLL is absent.
            FuseEditorBridge.NotifyEnterEditor();
        }

        [Fact]
        public void NotifyEditorExited_invokes_subscribers()
        {
            var counter = 0;
            Action handler = () => counter++;
            FuseEditorBridge.EditorExited += handler;

            try
            {
                FuseEditorBridge.NotifyEditorExited();
            }
            finally
            {
                FuseEditorBridge.EditorExited -= handler;
            }

            Assert.Equal(1, counter);
        }

        [Fact]
        public void NotifyEditorExited_invokes_every_subscriber()
        {
            var first = 0;
            var second = 0;
            Action a = () => first++;
            Action b = () => second++;

            FuseEditorBridge.EditorExited += a;
            FuseEditorBridge.EditorExited += b;

            try
            {
                FuseEditorBridge.NotifyEditorExited();
            }
            finally
            {
                FuseEditorBridge.EditorExited -= a;
                FuseEditorBridge.EditorExited -= b;
            }

            Assert.Equal(1, first);
            Assert.Equal(1, second);
        }

        [Fact]
        public void NotifyEditorExited_without_subscribers_does_not_throw()
        {
            // Bridge is reset to no-subscribers in the ctor; firing must
            // be safe so callers (FuseEditor.OnMapUnload, FuseEditor.Close)
            // never need to guard the call site.
            FuseEditorBridge.NotifyEditorExited();
        }

        [Fact]
        public void NotifyEditorExited_after_unsubscribe_does_not_fire_handler()
        {
            var counter = 0;
            Action handler = () => counter++;
            FuseEditorBridge.EditorExited += handler;
            FuseEditorBridge.EditorExited -= handler;

            FuseEditorBridge.NotifyEditorExited();

            Assert.Equal(0, counter);
        }

        [Fact]
        public void RegisterEditorProvider_sets_provider()
        {
            var provider = new FakeEditorProvider();

            FuseEditorBridge.RegisterEditorProvider(provider);

            Assert.Same(provider, FuseEditorBridge.EditorProvider);
        }

        [Fact]
        public void ClearEditorProvider_matching_reference_nulls_slot()
        {
            var provider = new FakeEditorProvider();
            FuseEditorBridge.RegisterEditorProvider(provider);

            FuseEditorBridge.ClearEditorProvider(provider);

            Assert.Null(FuseEditorBridge.EditorProvider);
        }

        [Fact]
        public void ClearEditorProvider_non_matching_reference_is_noop()
        {
            var registered = new FakeEditorProvider();
            var other = new FakeEditorProvider();
            FuseEditorBridge.RegisterEditorProvider(registered);

            FuseEditorBridge.ClearEditorProvider(other);

            Assert.Same(registered, FuseEditorBridge.EditorProvider);
        }

        [Fact]
        public void SelectionProvider_accepts_and_returns_assigned_value()
        {
            var provider = new FakeSelectionProvider();

            FuseEditorBridge.SelectionProvider = provider;

            Assert.Same(provider, FuseEditorBridge.SelectionProvider);
        }

        [Fact]
        public void IsEditorActive_accepts_and_returns_assigned_value()
        {
            FuseEditorBridge.IsEditorActive = true;
            Assert.True(FuseEditorBridge.IsEditorActive);

            FuseEditorBridge.IsEditorActive = false;
            Assert.False(FuseEditorBridge.IsEditorActive);
        }

        private sealed class FakeLifecycle : IFuseEditorLifecycle
        {
            public int LoadedCount { get; private set; }
            public int UnloadedCount { get; private set; }
            public int EnteredCount { get; private set; }

            public void OnFuseLoaded() => LoadedCount++;
            public void OnFuseUnloaded() => UnloadedCount++;
            public void EnterEditor() => EnteredCount++;
        }

        private sealed class FakeEditorProvider : IFuseEditorProvider
        {
            public void OnValidationCompleted(string objectId, ValidationResult result)
            {
            }
        }

        private sealed class FakeSelectionProvider : IFuseSelectionProvider
        {
            public string SelectedObjectId => null;
            public string SelectedObjectType => null;

            public void SelectObject(string id, string type)
            {
            }

            public void ClearSelection()
            {
            }
        }
    }
}
