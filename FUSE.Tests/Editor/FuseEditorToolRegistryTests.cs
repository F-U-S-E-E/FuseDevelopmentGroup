using System;
using System.Collections.Generic;
using FUSE.Editor.Screen.UI;
using FUSE.Editor.Track;
using Xunit;

namespace FUSE.Tests.Editor
{
    /// <summary>
    /// Locks the contract FuseEditorScreen consults at toolbar-render
    /// time: registration order is stable, SetActive fires lifecycle
    /// hooks in the right order, Tick only forwards to the active tool,
    /// and Reset cleanly empties state so an Exit-then-Enter cycle never
    /// accumulates entries.
    ///
    /// Registry state is static; the class serialises through
    /// <see cref="FuseEditorRegistryTestCollection"/> and resets in
    /// setup/teardown.
    /// </summary>
    [Collection(FuseEditorRegistryTestCollection.Name)]
    public sealed class FuseEditorToolRegistryTests : IDisposable
    {
        public FuseEditorToolRegistryTests()
        {
            FuseEditorToolRegistry.Reset();
        }

        public void Dispose()
        {
            FuseEditorToolRegistry.Reset();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void Default_state_is_empty_with_no_active_tool()
        {
            Assert.Null(FuseEditorToolRegistry.Active);
            Assert.Empty(FuseEditorToolRegistry.All);
        }

        [Fact]
        public void Register_appends_in_order()
        {
            var a = new FakeTool("a");
            var b = new FakeTool("b");
            var c = new FakeTool("c");

            FuseEditorToolRegistry.Register(a);
            FuseEditorToolRegistry.Register(b);
            FuseEditorToolRegistry.Register(c);

            Assert.Collection(FuseEditorToolRegistry.All,
                t => Assert.Same(a, t),
                t => Assert.Same(b, t),
                t => Assert.Same(c, t));
        }

        [Fact]
        public void Register_skips_duplicate_ids()
        {
            var first = new FakeTool("dupe");
            var second = new FakeTool("dupe");

            FuseEditorToolRegistry.Register(first);
            FuseEditorToolRegistry.Register(second);

            Assert.Single(FuseEditorToolRegistry.All);
            Assert.Same(first, FuseEditorToolRegistry.All[0]);
        }

        [Fact]
        public void Register_null_is_safe()
        {
            FuseEditorToolRegistry.Register(null);
            Assert.Empty(FuseEditorToolRegistry.All);
        }

        [Fact]
        public void SetActive_fires_OnActivate_on_incoming_tool()
        {
            var tool = new FakeTool("a");
            FuseEditorToolRegistry.Register(tool);

            FuseEditorToolRegistry.SetActive(tool);

            Assert.Same(tool, FuseEditorToolRegistry.Active);
            Assert.True(FuseEditorToolRegistry.IsActive(tool));
            Assert.Equal(1, tool.ActivateCount);
            Assert.Equal(0, tool.DeactivateCount);
        }

        [Fact]
        public void SetActive_fires_OnDeactivate_on_outgoing_tool()
        {
            var first = new FakeTool("first");
            var second = new FakeTool("second");
            FuseEditorToolRegistry.Register(first);
            FuseEditorToolRegistry.Register(second);

            FuseEditorToolRegistry.SetActive(first);
            FuseEditorToolRegistry.SetActive(second);

            Assert.Equal(1, first.ActivateCount);
            Assert.Equal(1, first.DeactivateCount);
            Assert.Equal(1, second.ActivateCount);
            Assert.Equal(0, second.DeactivateCount);
            Assert.Same(second, FuseEditorToolRegistry.Active);
        }

        [Fact]
        public void SetActive_to_same_tool_is_noop()
        {
            var tool = new FakeTool("a");
            FuseEditorToolRegistry.Register(tool);

            FuseEditorToolRegistry.SetActive(tool);
            FuseEditorToolRegistry.SetActive(tool);

            Assert.Equal(1, tool.ActivateCount);
            Assert.Equal(0, tool.DeactivateCount);
        }

        [Fact]
        public void SetActive_auto_registers_unknown_tool()
        {
            var tool = new FakeTool("orphan");

            FuseEditorToolRegistry.SetActive(tool);

            Assert.Same(tool, FuseEditorToolRegistry.Active);
            Assert.Single(FuseEditorToolRegistry.All);
            Assert.Equal(1, tool.ActivateCount);
        }

        [Fact]
        public void Deactivate_fires_OnDeactivate_and_clears_active()
        {
            var tool = new FakeTool("a");
            FuseEditorToolRegistry.SetActive(tool);

            FuseEditorToolRegistry.Deactivate();

            Assert.Null(FuseEditorToolRegistry.Active);
            Assert.Equal(1, tool.DeactivateCount);
        }

        [Fact]
        public void Reset_clears_active_and_list_and_fires_deactivate()
        {
            var a = new FakeTool("a");
            var b = new FakeTool("b");
            FuseEditorToolRegistry.Register(a);
            FuseEditorToolRegistry.Register(b);
            FuseEditorToolRegistry.SetActive(b);

            FuseEditorToolRegistry.Reset();

            Assert.Null(FuseEditorToolRegistry.Active);
            Assert.Empty(FuseEditorToolRegistry.All);
            Assert.Equal(1, b.DeactivateCount);
        }

        [Fact]
        public void TickActive_only_ticks_active_tool()
        {
            var a = new FakeTool("a");
            var b = new FakeTool("b");
            FuseEditorToolRegistry.Register(a);
            FuseEditorToolRegistry.Register(b);
            FuseEditorToolRegistry.SetActive(b);

            FuseEditorToolRegistry.TickActive();
            FuseEditorToolRegistry.TickActive();

            Assert.Equal(0, a.TickCount);
            Assert.Equal(2, b.TickCount);
        }

        [Fact]
        public void TickActive_without_active_tool_does_nothing()
        {
            // No throw, no crash, no harm.
            FuseEditorToolRegistry.TickActive();
        }

        [Fact]
        public void OnActivate_exception_does_not_propagate()
        {
            var tool = new FakeTool("throwing", throwOnActivate: true);

            // Should not throw — the registry catches + logs.
            FuseEditorToolRegistry.SetActive(tool);

            // The Active slot still landed on the tool even though
            // OnActivate threw: we set the field before invoking the
            // hook (see registry impl). This matches the Axiom intent
            // of "show the active state even if the tool's
            // initialization had issues."
            Assert.Same(tool, FuseEditorToolRegistry.Active);
        }

        private sealed class FakeTool : IFuseEditorTool
        {
            private readonly bool _throwOnActivate;

            public FakeTool(string id, bool throwOnActivate = false)
            {
                Id = id;
                _throwOnActivate = throwOnActivate;
            }

            public string Id { get; }
            public string LabelKey => "fake." + Id;
            public string IconGlyph => "?";
            public bool IsAvailable => true;
            public string UnavailableReason => null;

            public int ActivateCount { get; private set; }
            public int DeactivateCount { get; private set; }
            public int TickCount { get; private set; }

            public void OnActivate()
            {
                ActivateCount++;
                if (_throwOnActivate)
                {
                    throw new InvalidOperationException("simulated OnActivate failure");
                }
            }

            public void OnDeactivate() => DeactivateCount++;
            public void Tick() => TickCount++;
        }
    }
}
