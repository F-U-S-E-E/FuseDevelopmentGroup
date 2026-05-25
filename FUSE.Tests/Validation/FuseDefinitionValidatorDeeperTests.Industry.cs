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

        public class IndustryComponentDeepRules
        {
            private static FuseModDefinition WithComponent(FuseIndustryComponent component)
            {
                var definition = MinimalValid();
                definition.Operations.Industries["mill"] = new FuseIndustry
                {
                    Name = "Mill",
                    Components = new Dictionary<string, FuseIndustryComponent> { ["x"] = component }
                };
                return definition;
            }

            [Fact]
            public void UnknownNonCustomType_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "garbageType",
                    Name = "x"
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.component.type");
            }

            [Fact]
            public void DottedCustomType_EmitsWarning_NotError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "My.Custom.Component",
                    Name = "x"
                }));

                Assert.Contains(result.Warnings, w => w.Code == "fuse.operations.component.type.custom");
                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.operations.component.type");
            }

            [Fact]
            public void TypeUsingTrackSpans_WithoutSpans_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "loader",
                    Name = "x",
                    LoadId = "coal",
                    TrackSpanIds = null
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.component.trackSpanIds");
            }

            [Fact]
            public void PassengerStop_WithoutTrackSpans_IsAccepted()
            {
                // Legacy AMM allowed spanless passenger stops — locked in.
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "passengerStop",
                    Name = "x",
                    PassengerStopId = "stop-1",
                    TimetableCode = "T1",
                    TrackSpanIds = null
                }));

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.operations.component.trackSpanIds");
            }

            [Fact]
            public void LoaderType_WithoutLoadId_EmitsWarning()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "loader",
                    Name = "x",
                    LoadId = null,
                    TrackSpanIds = new[] { "span-1" }
                }));

                Assert.Contains(result.Warnings, w => w.Code == "fuse.operations.component.loadId");
            }

            [Fact]
            public void PassengerStop_WithoutLoadId_NoWarning()
            {
                // PassengerStop uses LoadId per the policy table, but the validator
                // explicitly suppresses the warning for it.
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "passengerStop",
                    Name = "x",
                    PassengerStopId = "stop-1",
                    TimetableCode = "T1",
                    LoadId = null
                }));

                Assert.DoesNotContain(result.Warnings, w => w.Code == "fuse.operations.component.loadId");
            }

            [Fact]
            public void NegativeStorageChangeRate_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "loader",
                    Name = "x",
                    LoadId = "coal",
                    TrackSpanIds = new[] { "s1" },
                    StorageChangeRate = -0.5f
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.component.storageChangeRate");
            }

            [Fact]
            public void NegativeMaxStorage_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "loader",
                    Name = "x",
                    LoadId = "coal",
                    TrackSpanIds = new[] { "s1" },
                    MaxStorage = -1f
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.component.maxStorage");
            }

            [Fact]
            public void NegativeCarTransferRate_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "loader",
                    Name = "x",
                    LoadId = "coal",
                    TrackSpanIds = new[] { "s1" },
                    CarTransferRate = -0.1f
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.component.carTransferRate");
            }

            [Fact]
            public void Formulaic_WithoutTerms_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "formulaic",
                    Name = "x",
                    InputTermsPerDay = null,
                    OutputTermsPerDay = null
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.formulaic.terms");
            }

            [Fact]
            public void TeamTrack_WithoutProfiles_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "teamTrack",
                    Name = "x",
                    TrackSpanIds = new[] { "s1" },
                    TeamProfiles = null
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.teamTrack.profile");
            }

            [Fact]
            public void PassengerStop_BlankIdOrTimetable_EmitsRequiredErrors()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "passengerStop",
                    Name = "x",
                    PassengerStopId = null,
                    TimetableCode = null
                }));

                Assert.Contains(result.Errors, e => e.Field.EndsWith(".passengerStopId") && e.Code == "fuse.required");
                Assert.Contains(result.Errors, e => e.Field.EndsWith(".timetableCode") && e.Code == "fuse.required");
            }

            [Fact]
            public void TeleportLoading_WithoutSpans_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "teleportLoading",
                    Name = "x",
                    InputSpanIds = null,
                    OutputSpanIds = null
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.teleportLoading.spans");
            }

            [Fact]
            public void TeleportLoading_NegativeCarLoadPeriod_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "teleportLoading",
                    Name = "x",
                    InputSpanIds = new[] { "s1" },
                    CarLoadPeriod = -1f
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.teleportLoading.carLoadPeriod");
            }

            [Fact]
            public void TeleportLoading_NegativeCarLengthFeet_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "teleportLoading",
                    Name = "x",
                    OutputSpanIds = new[] { "s1" },
                    CarLengthFeet = -10f
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.teleportLoading.carLengthFeet");
            }
        }

        public class InterchangeTransferRules
        {
            [Fact]
            public void BlankSourceKey_IsPreFilteredByNormalize_NotFlaggedByValidator()
            {
                // FuseMigration.NormalizeInterchangeTransfers strips blank-key entries
                // before the validator runs. That makes the validator's
                // "fuse.progression.interchangeTransfer.source.empty" rule effectively
                // unreachable through the public Validate() entry point. Locking in
                // the actual observable contract: no error surfaces, the entry is
                // silently dropped.
                var definition = MinimalValid();
                definition.Progression.Progressions["main"] = new FuseProgression
                {
                    Sections = new Dictionary<string, FuseSection>
                    {
                        ["s1"] = new FuseSection
                        {
                            DisplayName = "S1",
                            InterchangeTransfers = new Dictionary<string, string>
                            {
                                ["   "] = "destination"
                            }
                        }
                    }
                };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.progression.interchangeTransfer.source.empty");
                // Sanity: the normalized section's transfers dict is empty.
                var normalizedSection = definition.Progression.Progressions["main"].Sections["s1"];
                Assert.Empty(normalizedSection.InterchangeTransfers);
            }

            [Fact]
            public void SourceEqualsTarget_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Progression.Progressions["main"] = new FuseProgression
                {
                    Sections = new Dictionary<string, FuseSection>
                    {
                        ["s1"] = new FuseSection
                        {
                            DisplayName = "S1",
                            InterchangeTransfers = new Dictionary<string, string>
                            {
                                ["same"] = "Same"
                            }
                        }
                    }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.progression.interchangeTransfer.sameTarget");
            }
        }
    }
}
