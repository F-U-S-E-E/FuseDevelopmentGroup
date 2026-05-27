using Xunit;

namespace FUSE.Tests.Authoring.Editor
{
    /// <summary>
    /// Serializes every test class that mutates <c>FuseEditorBridge</c>'s
    /// static state plus <c>FuseEditorAssemblyLoader</c>'s
    /// <c>_initialized</c> flag. xUnit otherwise runs tests in different
    /// classes in parallel, which would race on the shared static
    /// provider slots and produce order-dependent flakes.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class FuseEditorBridgeTestCollection
    {
        public const string Name = "FuseEditorBridge";
    }
}
