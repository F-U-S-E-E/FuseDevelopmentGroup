using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// xUnit collection definition that serializes every test class
    /// that mutates <c>FuseAssetCollisionRegistry</c>'s static state.
    /// xUnit normally runs tests in different classes in parallel, which
    /// would corrupt the registry's static dictionaries and lead to
    /// flaky failures depending on test scheduling. Grouping every
    /// touching class under one collection forces them to run
    /// sequentially while still parallelizing against the rest of the
    /// test suite.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class FuseAssetCollisionRegistryTestCollection
    {
        public const string Name = "FuseAssetCollisionRegistry";
    }
}
