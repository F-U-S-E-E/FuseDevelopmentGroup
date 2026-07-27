using System;
using System.IO;
using System.Linq;
using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FusePrefabStoreSourceIdentifierIndexTests
    {
        [Fact]
        public void ReadTopLevelObjectIdentifiers_OnlyReturnsContainerObjectIdentifiers()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "fuse-prefab-index-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(
                    path,
                    "{\"objects\":[" +
                    "{\"identifier\":\"first\",\"definition\":{\"identifier\":\"nested\"}}," +
                    "{\"identifier\":\"second\",\"metadata\":{\"identifier\":\"also-nested\"}}" +
                    "]}");

                var identifiers =
                    FusePrefabStoreAssetPackContainingIdentifierTracePatch
                        .ReadTopLevelObjectIdentifiers(path)
                        .ToArray();

                Assert.Equal(new[] { "first", "second" }, identifiers);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
