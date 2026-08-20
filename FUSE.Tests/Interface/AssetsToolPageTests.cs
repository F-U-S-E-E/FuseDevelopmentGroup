using System.Linq;
using FUSE.Interface.MenuWindow;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Interface
{
    public class AssetsToolPageTests
    {
        [Fact]
        public void GroupDuplicateAssets_CollapsesKeysWithTheSameWinnerAndSources()
        {
            var groups = AssetsToolPage.GroupDuplicateAssets(new[]
            {
                Duplicate("mine", false, "RTM/primary", "RTM/nested"),
                Duplicate("engine-house", false, "RTM/primary", "RTM/nested"),
                Duplicate("sound-1", true, "Latoms", "lt-objects")
            });

            Assert.Equal(2, groups.Length);
            var identical = Assert.Single(groups, group => !group.DefinitionsDiffer);
            Assert.Equal(new[] { "mine", "engine-house" }, identical.Keys);
            Assert.Equal(new[] { "RTM/primary", "RTM/nested" }, identical.Sources);

            var different = Assert.Single(groups, group => group.DefinitionsDiffer);
            Assert.Equal(new[] { "sound-1" }, different.Keys);
        }

        [Fact]
        public void GroupDuplicateAssets_PreservesWinnerOrderAsPartOfTheGroupIdentity()
        {
            var groups = AssetsToolPage.GroupDuplicateAssets(new[]
            {
                Duplicate("a", false, "winner-a", "source-b"),
                Duplicate("b", false, "source-b", "winner-a")
            });

            Assert.Equal(2, groups.Length);
        }

        [Fact]
        public void BuildAssetSummary_ReportsGroupedImpactInsteadOfOnePreviewPerKey()
        {
            var diagnostics = new FuseAssetPackDiagnostics
            {
                UniqueAssetKeys = 10,
                DuplicateKeys = new[]
                {
                    Duplicate("mine", false, "RTM/primary", "RTM/nested"),
                    Duplicate("engine-house", false, "RTM/primary", "RTM/nested")
                }
            };

            var summary = AssetsToolPage.BuildAssetSummary(diagnostics);

            Assert.Contains("Overlapping asset keys: 2", summary);
            Assert.Contains("Source overlap groups: 1", summary);
            Assert.Contains("2 key(s)", summary);
            Assert.Contains("identical copies; no behavior change", summary);
            Assert.DoesNotContain("1 more duplicate key", summary);
        }

        private static FuseDuplicateAssetKey Duplicate(
            string key,
            bool definitionsDiffer,
            params string[] sources)
        {
            return new FuseDuplicateAssetKey
            {
                Key = key,
                DefinitionsDiffer = definitionsDiffer,
                Sources = sources
            };
        }
    }
}
