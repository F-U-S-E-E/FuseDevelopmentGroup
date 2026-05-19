using System.Linq;
using FUSE.Cache;
using Xunit;

namespace FUSE.Tests.Cache
{
    public class FuseRuntimeCacheBaseTests
    {
        // Test-only concrete subclass that exposes the abstract base's behavior
        // without dragging in any of the production index types' Rebuild logic.
        private sealed class TestCache : FuseRuntimeCacheBase<TestCache, string>
        {
            public override void Rebuild() { /* not exercised in these tests */ }
        }

        private static TestCache Fresh()
        {
            var cache = new TestCache();
            cache.Clear();
            return cache;
        }

        [Fact]
        public void NewCache_IsEmpty()
        {
            var cache = Fresh();

            Assert.Equal(0, cache.Count);
            Assert.Empty(cache.Ids);
            Assert.Empty(cache.Values);
        }

        [Fact]
        public void Set_AndIndexer_RoundTrip()
        {
            var cache = Fresh();

            cache.Set("a", "alpha");

            Assert.Equal("alpha", cache["a"]);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void Indexer_MissingKey_ReturnsNull()
        {
            var cache = Fresh();

            Assert.Null(cache["missing"]);
        }

        [Fact]
        public void TryGetValue_ReturnsTrue_AndPopulatesOut()
        {
            var cache = Fresh();
            cache.Set("a", "alpha");

            var found = cache.TryGetValue("a", out var value);

            Assert.True(found);
            Assert.Equal("alpha", value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryGetValue_BlankKey_ReturnsFalse(string key)
        {
            var cache = Fresh();
            cache.Set("a", "alpha");

            Assert.False(cache.TryGetValue(key, out var value));
            Assert.Null(value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Set_BlankKey_IsIgnored(string key)
        {
            var cache = Fresh();

            cache.Set(key, "value");

            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Set_NullValue_IsIgnored()
        {
            var cache = Fresh();

            cache.Set("a", null);

            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Remove_ExistingKey_ReturnsTrue_AndShrinks()
        {
            var cache = Fresh();
            cache.Set("a", "alpha");

            Assert.True(cache.Remove("a"));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Remove_MissingKey_ReturnsFalse()
        {
            var cache = Fresh();

            Assert.False(cache.Remove("missing"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Remove_BlankKey_ReturnsFalse(string key)
        {
            var cache = Fresh();
            cache.Set("a", "alpha");

            Assert.False(cache.Remove(key));
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void Clear_RemovesAllEntries()
        {
            var cache = Fresh();
            cache.Set("a", "alpha");
            cache.Set("b", "beta");

            cache.Clear();

            Assert.Equal(0, cache.Count);
            Assert.Empty(cache.Ids);
        }

        [Fact]
        public void Indexer_Setter_BypassesNullValueGuard_OfSet()
        {
            // Documenting the asymmetry: Set() rejects null values but the indexer setter
            // writes them through. This is the actual contract — locking it in so any
            // future tightening is a deliberate decision.
            var cache = Fresh();

            cache["a"] = null;

            Assert.Equal(1, cache.Count);
            Assert.Null(cache["a"]);
        }

        [Fact]
        public void Ids_And_Values_Reflect_CurrentContents()
        {
            var cache = Fresh();
            cache.Set("a", "alpha");
            cache.Set("b", "beta");

            Assert.Equal(new[] { "a", "b" }.OrderBy(s => s),
                         cache.Ids.OrderBy(s => s));
            Assert.Equal(new[] { "alpha", "beta" }.OrderBy(s => s),
                         cache.Values.OrderBy(s => s));
        }
    }
}
