using Xunit;

namespace FUSE.Tests.Editor
{
    /// <summary>
    /// Serialises every test class that mutates <c>FuseEditorWindowRegistry</c>
    /// (or any other static state shared by the editor UI). xUnit otherwise
    /// runs test classes in parallel, which would race on the shared
    /// per-kind visibility booleans and produce order-dependent flakes.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class FuseEditorRegistryTestCollection
    {
        public const string Name = "FuseEditorRegistry";
    }
}
