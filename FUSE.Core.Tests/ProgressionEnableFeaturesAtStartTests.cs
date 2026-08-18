using System;
using System.Linq;
using Fuse.Core.Model;
using Fuse.Core.Serialization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Fuse.Core.Tests
{
    /// <summary>
    /// Pins the wire contract of <c>progressions.&lt;id&gt;.enableFeaturesAtStart</c>.
    /// The runtime maps this field onto the game's private
    /// <c>Progression.enableFeaturesAtStart</c> array so a map mod can put its
    /// starting trunk on at Company start (and heal existing saves) via the
    /// same lever the base career uses. Two properties are load-bearing and
    /// must not drift:
    /// <list type="bullet">
    ///   <item>Omitted → null (no change), so every existing mod is unaffected.</item>
    ///   <item>Object form → per-id MERGE (union with the runtime's existing
    ///   list). A mod patching the base career progression <c>ewh</c> relies
    ///   on this so the base game's own start features (wh-el, ewh-intch)
    ///   survive; array form is a REPLACE and would wipe them.</item>
    /// </list>
    /// </summary>
    public class ProgressionEnableFeaturesAtStartTests
    {
        private const string Skeleton = @"{
  ""schemaVersion"": ""1.0"",
  ""id"": ""Test.EnableFeaturesAtStart"",
  ""progression"": {
    ""progressions"": {
      ""ewh"": PROGRESSION
    }
  }
}";

        private static FuseProgression Load(string progressionJson)
        {
            var json = Skeleton.Replace("PROGRESSION", progressionJson);
            var definition = FuseCoreSerializer.FromJson(json);
            Assert.NotNull(definition?.Progression?.Progressions);
            Assert.True(definition.Progression.Progressions.ContainsKey("ewh"));
            return definition.Progression.Progressions["ewh"];
        }

        [Fact]
        public void Omitted_IsNull_SoExistingModsAreUnaffected()
        {
            var progression = Load(@"{ ""sections"": {} }");

            Assert.Null(progression.EnableFeaturesAtStart);
        }

        [Fact]
        public void ObjectForm_DeserializesAsMergePatch()
        {
            var progression = Load(@"{ ""enableFeaturesAtStart"": { ""APPA-Start-Seed"": true, ""AR-Connelly"": true }, ""sections"": {} }");

            var patch = progression.EnableFeaturesAtStart;
            Assert.NotNull(patch);
            Assert.True(patch.HasValue);
            Assert.Null(patch.Set);
            Assert.NotNull(patch.Patch);
            Assert.True(patch.Patch["APPA-Start-Seed"]);
            Assert.True(patch.Patch["AR-Connelly"]);
        }

        [Fact]
        public void ArrayForm_DeserializesAsReplacementSet()
        {
            var progression = Load(@"{ ""enableFeaturesAtStart"": [ ""APPA-Start-Seed"" ], ""sections"": {} }");

            var patch = progression.EnableFeaturesAtStart;
            Assert.NotNull(patch);
            Assert.True(patch.HasValue);
            Assert.Null(patch.Patch);
            Assert.Equal(new[] { "APPA-Start-Seed" }, patch.Set);
        }

        [Fact]
        public void ObjectForm_MergesWithBaseCareerStartFeatures()
        {
            // The exact production case: the mod patches the base career
            // progression 'ewh', whose runtime enableFeaturesAtStart already
            // holds the base game's own start features. The merge must keep
            // them and add the mod's trunk features.
            var progression = Load(@"{ ""enableFeaturesAtStart"": {
                ""APPA-Start-Seed"": true,
                ""Ela-Whittier"": true,
                ""AR-Whittier-Sawmill"": true,
                ""AR-Connelly-Y"": true,
                ""AR-Connelly"": true
            }, ""sections"": {} }");

            var existingBaseGame = new[] { "wh-el", "ewh-intch" };
            var result = progression.EnableFeaturesAtStart.ApplyTo(existingBaseGame);

            var expected = new[]
            {
                "wh-el", "ewh-intch",
                "APPA-Start-Seed", "Ela-Whittier", "AR-Whittier-Sawmill", "AR-Connelly-Y", "AR-Connelly"
            };
            Assert.Equal(
                expected.OrderBy(id => id, StringComparer.OrdinalIgnoreCase),
                result.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void ObjectForm_FalseRemovesOnlyThatFeature()
        {
            // The idPatch contract: false is a per-id removal. It must remove
            // exactly the named id and leave every other existing entry alone
            // (both the base game's and the mod's own additions).
            var progression = Load(@"{ ""enableFeaturesAtStart"": { ""ewh-intch"": false, ""APPA-Start-Seed"": true }, ""sections"": {} }");

            var result = progression.EnableFeaturesAtStart.ApplyTo(new[] { "wh-el", "ewh-intch" });

            Assert.Equal(
                new[] { "APPA-Start-Seed", "wh-el" },
                result.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
            Assert.DoesNotContain("ewh-intch", result, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void ArrayForm_ReplacesBaseCareerStartFeatures()
        {
            // Documents the hazard the merge form exists to avoid: array form
            // is a wholesale replacement and drops the base game's entries.
            var progression = Load(@"{ ""enableFeaturesAtStart"": [ ""APPA-Start-Seed"" ], ""sections"": {} }");

            var result = progression.EnableFeaturesAtStart.ApplyTo(new[] { "wh-el", "ewh-intch" });

            Assert.Equal(new[] { "APPA-Start-Seed" }, result);
        }

        [Fact]
        public void RoundTrip_PreservesObjectForm()
        {
            // The converter passes the field through verbatim; the serializer
            // must not degrade the merge dict into an array on re-emit, or a
            // re-saved package would silently switch from merge to replace.
            var progression = Load(@"{ ""enableFeaturesAtStart"": { ""APPA-Start-Seed"": true }, ""sections"": {} }");
            var definition = new FuseModDefinition
            {
                Id = "Test.EnableFeaturesAtStart",
                SchemaVersion = "1.0",
                Progression = new FuseProgressionRoot()
            };
            definition.Progression.Progressions["ewh"] = progression;

            var json = FuseCoreSerializer.ToJson(definition);
            var reparsed = JObject.Parse(json);
            var field = reparsed.SelectToken("progression.progressions.ewh.enableFeaturesAtStart");

            Assert.NotNull(field);
            Assert.Equal(JTokenType.Object, field.Type);
            Assert.True(field.Value<bool>("APPA-Start-Seed"));
        }
    }
}
