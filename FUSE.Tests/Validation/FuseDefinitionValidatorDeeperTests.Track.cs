using System.Collections.Generic;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Authoring.Validation;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Validation
{
    public partial class FuseDefinitionValidatorDeeperTests
    {

        public class TrackLocationRules
        {
            private static FuseModDefinition WithSpan(FuseTrackLocation upper, FuseTrackLocation lower)
            {
                var definition = MinimalValid();
                definition.Tracks.Spans["sp1"] = new FuseSpan { Upper = upper, Lower = lower };
                return definition;
            }

            private static FuseTrackLocation Loc(string segmentId, float? normalized = null, float? distance = null, string end = null)
            {
                return new FuseTrackLocation
                {
                    SegmentId = segmentId,
                    Normalized = normalized,
                    Distance = distance,
                    End = end
                };
            }

            [Fact]
            public void NullUpperOrLower_EmitsError()
            {
                var definition = WithSpan(null, Loc("seg-1", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.required" && e.Field == "tracks.spans.sp1.upper");
            }

            [Fact]
            public void BlankSegmentId_EmitsRequiredError()
            {
                var definition = WithSpan(Loc(null, normalized: 0.1f), Loc("seg-1", normalized: 0.9f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "tracks.spans.sp1.upper.segmentId" && e.Code == "fuse.required");
            }

            [Fact]
            public void Neither_Normalized_Nor_Distance_EmitsError()
            {
                var definition = WithSpan(Loc("seg-1"), Loc("seg-1", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.measure");
            }

            [Fact]
            public void Both_Normalized_And_Distance_EmitsExclusiveError()
            {
                var definition = WithSpan(
                    Loc("seg-1", normalized: 0.5f, distance: 10f),
                    Loc("seg-1", normalized: 0.7f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.measure.exclusive");
            }

            [Theory]
            [InlineData(-0.1f)]
            [InlineData(1.5f)]
            public void NormalizedOutsideRange_EmitsError(float normalized)
            {
                var definition = WithSpan(
                    Loc("seg-1", normalized: normalized),
                    Loc("seg-1", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.normalized");
            }

            [Theory]
            [InlineData(0f)]
            [InlineData(0.5f)]
            [InlineData(1f)]
            public void NormalizedInRange_NoError(float normalized)
            {
                var definition = WithSpan(
                    Loc("seg-1", normalized: normalized),
                    Loc("seg-2", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.track.location.normalized");
            }

            [Fact]
            public void NegativeDistance_EmitsError()
            {
                var definition = WithSpan(
                    Loc("seg-1", distance: -1f),
                    Loc("seg-1", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.distance");
            }

            [Theory]
            [InlineData("A")]
            [InlineData("B")]
            [InlineData("Start")]
            [InlineData("END")]
            [InlineData("  start  ")] // trimmed
            public void Valid_End_Tokens_NoError(string endToken)
            {
                var definition = WithSpan(
                    Loc("seg-1", normalized: 0.5f, end: endToken),
                    Loc("seg-2", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.track.location.end");
            }

            [Theory]
            [InlineData("X")]
            [InlineData("middle")]
            [InlineData("C")]
            public void Invalid_End_Tokens_EmitError(string endToken)
            {
                var definition = WithSpan(
                    Loc("seg-1", normalized: 0.5f, end: endToken),
                    Loc("seg-2", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.end");
            }

            [Fact]
            public void ExternalSegmentReference_EmitsWarning()
            {
                var definition = WithSpan(
                    Loc("external-segment", normalized: 0.5f),
                    Loc("another-external", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.track.segment.external");
            }
        }

        public class SameSegmentSpanRules
        {
            private static FuseModDefinition WithSameSegmentSpan(string upperEnd, string lowerEnd, float upperNormalized = 0.2f, float lowerNormalized = 0.8f)
            {
                var definition = MinimalValid();
                definition.Tracks.Nodes["n1"] = new FuseNode { Position = new Vector3(0, 0, 0) };
                definition.Tracks.Nodes["n2"] = new FuseNode { Position = new Vector3(100, 0, 0) };
                definition.Tracks.Segments["seg-1"] = new FuseSegment
                {
                    StartNodeId = "n1",
                    EndNodeId = "n2"
                };
                definition.Tracks.Spans["sp1"] = new FuseSpan
                {
                    Upper = new FuseTrackLocation { SegmentId = "seg-1", Normalized = upperNormalized, End = upperEnd },
                    Lower = new FuseTrackLocation { SegmentId = "seg-1", Normalized = lowerNormalized, End = lowerEnd }
                };
                return definition;
            }

            [Fact]
            public void SameSegment_SameEnd_EmitsWarning()
            {
                // Both endpoints face "A" — legacy-compatible but flagged.
                var definition = WithSameSegmentSpan(upperEnd: "A", lowerEnd: "A");

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.track.span.sameSegment.sameDirection");
            }

            [Fact]
            public void SameSegment_OppositeEnds_NoSameDirectionWarning()
            {
                var definition = WithSameSegmentSpan(upperEnd: "A", lowerEnd: "B");

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Warnings, w => w.Code == "fuse.track.span.sameSegment.sameDirection");
            }

            [Fact]
            public void DifferentSegments_DoNotTriggerSameSegmentChecks()
            {
                var definition = MinimalValid();
                definition.Tracks.Segments["seg-1"] = new FuseSegment { StartNodeId = "n1", EndNodeId = "n2" };
                definition.Tracks.Segments["seg-2"] = new FuseSegment { StartNodeId = "n3", EndNodeId = "n4" };
                definition.Tracks.Spans["sp1"] = new FuseSpan
                {
                    Upper = new FuseTrackLocation { SegmentId = "seg-1", Normalized = 0.5f, End = "A" },
                    Lower = new FuseTrackLocation { SegmentId = "seg-2", Normalized = 0.5f, End = "A" }
                };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Warnings, w => w.Code == "fuse.track.span.sameSegment.sameDirection");
            }

            [Fact]
            public void SameSegment_DistanceOutsideEstimatedLength_EmitsWarning()
            {
                // Nodes 100 units apart; distance of 500 is well outside the
                // straight-line estimate. The validator emits a warning that
                // runtime will use the actual curved length.
                var definition = MinimalValid();
                definition.Tracks.Nodes["n1"] = new FuseNode { Position = new Vector3(0, 0, 0) };
                definition.Tracks.Nodes["n2"] = new FuseNode { Position = new Vector3(100, 0, 0) };
                definition.Tracks.Segments["seg-1"] = new FuseSegment { StartNodeId = "n1", EndNodeId = "n2" };
                definition.Tracks.Spans["sp1"] = new FuseSpan
                {
                    Upper = new FuseTrackLocation { SegmentId = "seg-1", Distance = 500f, End = "A" },
                    Lower = new FuseTrackLocation { SegmentId = "seg-1", Distance = 10f, End = "B" }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.track.span.upper.distance");
            }
        }
    }
}
