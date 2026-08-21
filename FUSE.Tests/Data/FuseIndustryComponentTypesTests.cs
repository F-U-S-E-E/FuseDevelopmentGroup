using FUSE.Authoring.Data;
using Xunit;

namespace FUSE.Tests.Data
{
    public class FuseIndustryComponentTypesTests
    {
        public class NormalizeTests
        {
            [Theory]
            [InlineData("loader", "loader")]
            [InlineData("industryloader", "loader")]
            [InlineData("model.ops.industryloader", "loader")]
            [InlineData("unloader", "unloader")]
            [InlineData("model.ops.industryunloader", "unloader")]
            [InlineData("formulaic", "formulaic")]
            [InlineData("model.ops.formulaicindustrycomponent", "formulaic")]
            [InlineData("repairtrack", "repairTrack")]
            [InlineData("repair-track", "repairTrack")]
            [InlineData("teamtrack", "teamTrack")]
            [InlineData("team-track", "teamTrack")]
            [InlineData("interchange", "interchange")]
            [InlineData("interchangedloader", "interchangedLoader")]
            [InlineData("interchanged-loader", "interchangedLoader")]
            [InlineData("interchangedunloader", "interchangedUnloader")]
            [InlineData("teleportloading", "teleportLoading")]
            [InlineData("teleport-loading", "teleportLoading")]
            [InlineData("teleportloadingindustry", "teleportLoading")]
            [InlineData("progression", "progression")]
            [InlineData("progressionindustry", "progression")]
            [InlineData("passengerstop", "passengerStop")]
            [InlineData("passenger-stop", "passengerStop")]
            [InlineData("paxstationcomponent", "passengerStop")]
            [InlineData("alinasmapmod.paxstationcomponent", "passengerStop")]
            [InlineData("ADRFDR.Pay4Resource", "ConfusingSupplements.IndustryComponents.Pay4Resource")]
            public void Known_Aliases_NormalizeToCanonical(string input, string expected)
            {
                Assert.Equal(expected, FuseIndustryComponentTypes.Normalize(input));
            }

            [Theory]
            [InlineData("LOADER", "loader")]
            [InlineData("UnLoadeR", "unloader")]
            public void Aliases_AreCaseInsensitive(string input, string expected)
            {
                Assert.Equal(expected, FuseIndustryComponentTypes.Normalize(input));
            }

            [Fact]
            public void TrimsSurroundingWhitespace()
            {
                Assert.Equal("loader", FuseIndustryComponentTypes.Normalize("  loader  "));
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("   ")]
            public void NullOrBlankInput_ReturnsAsIs(string input)
            {
                Assert.Equal(input, FuseIndustryComponentTypes.Normalize(input));
            }

            [Fact]
            public void UnknownInput_TrimmedReturnedUnchanged()
            {
                Assert.Equal("MyCustom.Type", FuseIndustryComponentTypes.Normalize("  MyCustom.Type  "));
            }

            [Fact]
            public void CaptiveConversionLoaderAlias_MapsToConfusingSupplementsCanonical()
            {
                // Documents that some legacy aliases map to a fully-qualified non-canonical
                // string (a custom-type candidate), not a built-in.
                Assert.Equal(
                    "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader",
                    FuseIndustryComponentTypes.Normalize("captiveconversionloader"));
            }
        }

        public class IsKnownTests
        {
            [Theory]
            [InlineData("loader")]
            [InlineData("unloader")]
            [InlineData("formulaic")]
            [InlineData("repairTrack")]
            [InlineData("teamTrack")]
            [InlineData("interchange")]
            [InlineData("interchangedLoader")]
            [InlineData("interchangedUnloader")]
            [InlineData("teleportLoading")]
            [InlineData("progression")]
            [InlineData("passengerStop")]
            public void CanonicalNames_AreKnown(string type)
            {
                Assert.True(FuseIndustryComponentTypes.IsKnown(type));
            }

            [Theory]
            [InlineData("industryloader")] // alias normalizes to loader
            [InlineData("paxstationcomponent")] // alias normalizes to passengerStop
            public void AliasesOfCanonicals_AreKnown(string type)
            {
                Assert.True(FuseIndustryComponentTypes.IsKnown(type));
            }

            [Theory]
            [InlineData("captiveconversionloader")] // alias to non-canonical custom type
            [InlineData("Some.Custom.Component")]
            [InlineData("nonsense")]
            [InlineData("")]
            [InlineData(null)]
            public void NonCanonical_IsNotKnown(string type)
            {
                Assert.False(FuseIndustryComponentTypes.IsKnown(type));
            }
        }

        public class IsCustomTypeCandidateTests
        {
            [Theory]
            [InlineData("Some.Custom.Component", true)]
            [InlineData("My.Mod.Industries.Foo", true)]
            [InlineData("captiveconversionloader", true)] // alias resolves to a dotted custom string
            [InlineData("loader", false)] // canonical, not a candidate
            [InlineData("nodotsinhere", false)] // no dot, unknown
            [InlineData("", false)]
            [InlineData(null, false)]
            public void RecognizesDottedNonCanonicalIdentifiers(string type, bool expected)
            {
                Assert.Equal(expected, FuseIndustryComponentTypes.IsCustomTypeCandidate(type));
            }
        }

        public class UsesLoadIdTests
        {
            [Theory]
            [InlineData("loader", true)]
            [InlineData("unloader", true)]
            [InlineData("repairTrack", true)]
            [InlineData("interchangedLoader", true)]
            [InlineData("interchangedUnloader", true)]
            [InlineData("passengerStop", true)]
            [InlineData("formulaic", false)]
            [InlineData("teamTrack", false)]
            [InlineData("interchange", false)]
            [InlineData("teleportLoading", false)]
            [InlineData("progression", false)]
            [InlineData("unknown", false)]
            public void Returns_Expected(string type, bool expected)
            {
                Assert.Equal(expected, FuseIndustryComponentTypes.UsesLoadId(type));
            }
        }

        public class UsesTrackSpanIdsTests
        {
            [Theory]
            [InlineData("loader", true)]
            [InlineData("unloader", true)]
            [InlineData("repairTrack", true)]
            [InlineData("teamTrack", true)]
            [InlineData("interchange", true)]
            [InlineData("interchangedLoader", true)]
            [InlineData("interchangedUnloader", true)]
            [InlineData("progression", true)]
            [InlineData("passengerStop", true)]
            [InlineData("formulaic", false)]
            [InlineData("teleportLoading", false)]
            [InlineData("unknown", false)]
            public void Returns_Expected(string type, bool expected)
            {
                Assert.Equal(expected, FuseIndustryComponentTypes.UsesTrackSpanIds(type));
            }
        }

        [Fact]
        public void KnownTypesForMessage_IncludesAllCanonicals_AndIsSortedOrdinalIgnoreCase()
        {
            var message = FuseIndustryComponentTypes.KnownTypesForMessage();

            foreach (var type in FuseIndustryComponentTypes.Canonical)
            {
                Assert.Contains(type, message);
            }
            // Sanity: alphabetical-ish order. "formulaic" should come before "loader".
            Assert.True(message.IndexOf("formulaic") < message.IndexOf("loader"));
        }
    }
}
