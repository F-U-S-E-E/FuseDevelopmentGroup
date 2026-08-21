using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseLocationPickerDeduplicationPatchTests
    {
        [Fact]
        public void DeduplicateByKey_CollapsesEquivalentKeysAndPreservesOrder()
        {
            var source = new[] { "sawmill-mp1", "SAWMILL-MP1", "sawmill-mp2" };

            var removed = FuseLocationPickerDeduplicationPatch.DeduplicateByKey(
                source,
                value => value,
                out var result);

            Assert.Equal(1, removed);
            Assert.Equal(new[] { "sawmill-mp1", "sawmill-mp2" }, result);
        }

        [Fact]
        public void DeduplicateByKey_PreservesRowsWhoseIdentityCannotBeResolved()
        {
            var source = new[] { "broken-one", "broken-two" };

            var removed = FuseLocationPickerDeduplicationPatch.DeduplicateByKey(
                source,
                _ => null,
                out var result);

            Assert.Equal(0, removed);
            Assert.Equal(source, result);
        }

        [Fact]
        public void DeduplicateByKey_PreservesSourceWhenKeySelectorIsNull()
        {
            var source = new[] { "sawmill-mp1", "sawmill-mp2" };

            var removed = FuseLocationPickerDeduplicationPatch.DeduplicateByKey(
                source,
                null,
                out var result);

            Assert.Equal(0, removed);
            Assert.Equal(source, result);
        }

        [Fact]
        public void DeduplicateByKey_ReturnsEmptyResultForNullSource()
        {
            var removed = FuseLocationPickerDeduplicationPatch.DeduplicateByKey<string>(
                null,
                value => value,
                out var result);

            Assert.Equal(0, removed);
            Assert.Empty(result);
        }

        [Fact]
        public void ResolveCanonicalByKey_ReturnsRetainedEquivalentInstance()
        {
            var retained = new Item("sawmill-mp1", 1);
            var duplicate = new Item("SAWMILL-MP1", 2);

            var selected = FuseLocationPickerDeduplicationPatch.ResolveCanonicalByKey(
                duplicate,
                new[] { retained },
                item => item.Id);

            Assert.Same(retained, selected);
        }

        private sealed class Item
        {
            internal Item(string id, int instance)
            {
                Id = id;
                Instance = instance;
            }

            internal string Id { get; }
            internal int Instance { get; }
        }
    }
}
