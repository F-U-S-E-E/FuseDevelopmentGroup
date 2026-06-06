using System;
using System.Linq;
using FUSE.Editor.Screen.UI;
using Xunit;

namespace FUSE.Tests.Editor
{
    /// <summary>
    /// Locks the contract <see cref="FuseEditorScreen"/> consults at
    /// render time: every <see cref="FuseEditorWindowKind"/> starts at its
    /// default visibility, mutations through <c>SetOpen</c> / <c>Toggle</c>
    /// survive lookups, and the iteration order is stable so a windows-
    /// toggle menu built from <c>All()</c> doesn't shuffle between renders.
    ///
    /// Registry state is static (modelled on Axiom's per-enum state), so
    /// every test resets to defaults in the ctor + dispose and the class
    /// joins the <c>FuseEditorRegistry</c> serialisation collection to
    /// avoid races.
    /// </summary>
    [Collection(FuseEditorRegistryTestCollection.Name)]
    public sealed class FuseEditorWindowRegistryTests : IDisposable
    {
        public FuseEditorWindowRegistryTests()
        {
            FuseEditorWindowRegistry.ResetToDefaults();
        }

        public void Dispose()
        {
            FuseEditorWindowRegistry.ResetToDefaults();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void Default_state_matches_OpenByDefault_per_kind()
        {
            foreach (var kind in FuseEditorWindowRegistry.All())
            {
                Assert.Equal(
                    FuseEditorWindowRegistry.OpenByDefault(kind),
                    FuseEditorWindowRegistry.IsOpen(kind));
            }
        }

        [Fact]
        public void SetOpen_flips_visibility()
        {
            FuseEditorWindowRegistry.SetOpen(FuseEditorWindowKind.EntityTree, false);
            Assert.False(FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.EntityTree));

            FuseEditorWindowRegistry.SetOpen(FuseEditorWindowKind.EntityTree, true);
            Assert.True(FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.EntityTree));
        }

        [Fact]
        public void SetOpen_is_idempotent()
        {
            FuseEditorWindowRegistry.SetOpen(FuseEditorWindowKind.Properties, false);
            FuseEditorWindowRegistry.SetOpen(FuseEditorWindowKind.Properties, false);

            Assert.False(FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.Properties));
        }

        [Fact]
        public void Toggle_flips_state()
        {
            var initial = FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.ToolStrip);

            FuseEditorWindowRegistry.Toggle(FuseEditorWindowKind.ToolStrip);
            Assert.Equal(!initial, FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.ToolStrip));

            FuseEditorWindowRegistry.Toggle(FuseEditorWindowKind.ToolStrip);
            Assert.Equal(initial, FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.ToolStrip));
        }

        [Fact]
        public void ResetToDefaults_restores_initial_visibility()
        {
            // Mutate everything off, then reset and assert every kind is
            // back to its registered OpenByDefault value.
            foreach (var kind in FuseEditorWindowRegistry.All())
            {
                FuseEditorWindowRegistry.SetOpen(kind, false);
            }

            FuseEditorWindowRegistry.ResetToDefaults();

            foreach (var kind in FuseEditorWindowRegistry.All())
            {
                Assert.Equal(
                    FuseEditorWindowRegistry.OpenByDefault(kind),
                    FuseEditorWindowRegistry.IsOpen(kind));
            }
        }

        [Fact]
        public void All_covers_every_enum_value()
        {
            var declared = (FuseEditorWindowKind[])Enum.GetValues(typeof(FuseEditorWindowKind));
            var registered = FuseEditorWindowRegistry.All().ToArray();

            Assert.Equal(declared.Length, registered.Length);
            foreach (var kind in declared)
            {
                Assert.Contains(kind, registered);
            }
        }

        [Fact]
        public void All_iteration_order_is_stable_across_calls()
        {
            // Important: the windows-toggle popup iterates this collection
            // every frame. A non-stable order would shuffle the row layout
            // visibly between draws.
            var first = FuseEditorWindowRegistry.All().ToArray();
            var second = FuseEditorWindowRegistry.All().ToArray();

            Assert.Equal(first, second);
        }

        [Fact]
        public void NameKey_returns_registered_key_for_known_kind()
        {
            var key = FuseEditorWindowRegistry.NameKey(FuseEditorWindowKind.EntityTree);

            Assert.False(string.IsNullOrEmpty(key));
            // The registered key follows the dotted convention.
            Assert.StartsWith("fuse.editor.window.", key);
        }

        [Fact]
        public void IsImportant_is_true_for_every_currently_registered_kind()
        {
            // Until non-important panels land (operation dialogs etc.),
            // every kind should report important so it appears in the
            // toggle menu. This test acts as a tripwire when that
            // expectation changes.
            foreach (var kind in FuseEditorWindowRegistry.All())
            {
                Assert.True(
                    FuseEditorWindowRegistry.IsImportant(kind),
                    $"{kind} should be Important until non-important panels are introduced.");
            }
        }
    }
}
