using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class IndustryAPI
    {

        private static void ApplyPartialComponentDefinition(IndustryComponent component, FuseIndustryComponent definition)
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var isPassengerStop = component is FusePassengerStopComponent;
            if (!string.IsNullOrWhiteSpace(definition.Name))
            {
                component.name = definition.Name;
            }

            if (HasStringListPatch(definition.TrackSpanPatch))
            {
                component.trackSpans = ApplyTrackSpanPatch(component.trackSpans, definition.TrackSpanPatch);
            }
            else if (definition.TrackSpanIds != null && definition.TrackSpanIds.Length > 0)
            {
                component.trackSpans = MergeSpans(component.trackSpans, ResolveSpans(definition.TrackSpanIds));
            }

            if (definition.CarTypeFilter != null)
            {
                component.carTypeFilter = new CarTypeFilter(ResolveCarTypeFilter(component, definition.CarTypeFilter, isPassengerStop));
            }

            var effectiveLoadId = isPassengerStop && string.IsNullOrWhiteSpace(definition.LoadId)
                ? null
                : definition.LoadId;
            var hasLoadPatch = !string.IsNullOrWhiteSpace(effectiveLoadId);
            var load = hasLoadPatch ? ResolveLoad(effectiveLoadId) : null;

            var loader = component as IndustryLoader;
            if (loader != null)
            {
                if (hasLoadPatch)
                {
                    loader.load = load;
                }

                loader.productionRate = definition.StorageChangeRate ?? loader.productionRate;
                loader.maxStorage = definition.MaxStorage ?? loader.maxStorage;
                loader.carLoadRate = definition.CarTransferRate ?? loader.carLoadRate;
                loader.orderEmpties = definition.OrderAroundEmpties ?? loader.orderEmpties;
                loader.orderAwayLoaded = definition.OrderAroundLoaded ?? loader.orderAwayLoaded;
                return;
            }

            var unloader = component as IndustryUnloader;
            if (unloader != null)
            {
                if (hasLoadPatch)
                {
                    unloader.load = load;
                }

                unloader.storageConsumptionRate = definition.StorageChangeRate ?? unloader.storageConsumptionRate;
                unloader.maxStorage = definition.MaxStorage ?? unloader.maxStorage;
                unloader.carUnloadRate = definition.CarTransferRate ?? unloader.carUnloadRate;
                unloader.orderAwayEmpties = definition.OrderAroundEmpties ?? unloader.orderAwayEmpties;
                unloader.orderLoads = definition.OrderAroundLoaded ?? unloader.orderLoads;
                return;
            }

            var formulaic = component as FormulaicIndustryComponent;
            if (formulaic != null)
            {
                if (definition.InputTermsPerDay != null && definition.InputTermsPerDay.Count > 0)
                {
                    formulaic.inputTerms = BuildFormulaTerms(definition.InputTermsPerDay);
                }

                if (definition.OutputTermsPerDay != null && definition.OutputTermsPerDay.Count > 0)
                {
                    formulaic.outputTerms = BuildFormulaTerms(definition.OutputTermsPerDay);
                }

                return;
            }

            var repairTrack = component as RepairTrack;
            if (repairTrack != null)
            {
                if (hasLoadPatch && load != null)
                {
                    RepairPartsLoadField?.SetValue(repairTrack, load);
                }

                if (definition.CanOverhaul != null)
                {
                    repairTrack.canOverhaul = definition.CanOverhaul.Value;
                }

                return;
            }

            var teamTrack = component as TeamTrack;
            if (teamTrack != null)
            {
                teamTrack.idealCars = definition.IdealCars ?? teamTrack.idealCars;
                if (definition.TeamProfiles != null && definition.TeamProfiles.Count > 0)
                {
                    teamTrack.profile = BuildTeamTrackProfile(definition.TeamProfiles);
                }

                return;
            }

            var interchangedLoader = component as InterchangedIndustryLoader;
            if (interchangedLoader != null)
            {
                if (hasLoadPatch)
                {
                    interchangedLoader.load = load;
                }

                return;
            }

            var fuseInterchangedUnloader = component as FuseInterchangedIndustryUnloader;
            if (fuseInterchangedUnloader != null)
            {
                if (hasLoadPatch)
                {
                    fuseInterchangedUnloader.load = load;
                }

                return;
            }

            if (TryApplyOptionalType(component, "Model.Ops.InterchangedIndustryUnloader", obj =>
            {
                if (hasLoadPatch)
                {
                    ApplyOptionalLoadField(obj, load);
                }
            }))
            {
                return;
            }

            if (TryApplyOptionalType(component, "Model.Ops.TeleportLoadingIndustry", obj =>
            {
                if (hasLoadPatch)
                {
                    ApplyOptionalLoadField(obj, load);
                }

                ApplyPartialTeleportLoadingFields(obj, definition);
            }))
            {
                return;
            }

            var passengerStop = component as FusePassengerStopComponent;
            if (passengerStop != null)
            {
                passengerStop.PassengerStopId = definition.PassengerStopId ?? passengerStop.PassengerStopId;
                if (hasLoadPatch)
                {
                    passengerStop.PassengerLoad = load;
                }

                passengerStop.TimetableCode = definition.TimetableCode ?? passengerStop.TimetableCode;
                passengerStop.BasePopulation = definition.BasePopulation ?? passengerStop.BasePopulation;
                passengerStop.NeighborIds = definition.NeighborIds ?? passengerStop.NeighborIds;
                passengerStop.Branch = definition.Branch ?? passengerStop.Branch;
                passengerStop.BranchDefinitions = definition.BranchDefinitions ?? passengerStop.BranchDefinitions;
            }

            ApplyCustomIndustryComponentFields(component, definition, load);
            var appliedComponent = component as IFuseAppliedComponent;
            appliedComponent?.OnFuseDefinitionApplied();
        }

        private static void ApplyComponentDefinition(IndustryComponent component, FuseIndustryComponent definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var isPassengerStop = component is FusePassengerStopComponent;
            component.name = string.IsNullOrWhiteSpace(definition.Name) ? component.subIdentifier : definition.Name;
            component.trackSpans = HasStringListPatch(definition.TrackSpanPatch)
                ? ApplyTrackSpanPatch(component.trackSpans, definition.TrackSpanPatch)
                : ResolveSpans(definition.TrackSpanIds);
            component.carTypeFilter = new CarTypeFilter(ResolveCarTypeFilter(component, definition.CarTypeFilter, isPassengerStop));
            component.sharedStorage = definition.SharedStorage;

            var effectiveLoadId = isPassengerStop && string.IsNullOrWhiteSpace(definition.LoadId)
                ? "passengers"
                : definition.LoadId;
            var load = ResolveLoad(effectiveLoadId);
            var loader = component as IndustryLoader;
            if (loader != null)
            {
                loader.load = load;
                loader.productionRate = definition.StorageChangeRate ?? loader.productionRate;
                loader.maxStorage = definition.MaxStorage ?? loader.maxStorage;
                loader.carLoadRate = definition.CarTransferRate ?? loader.carLoadRate;
                loader.orderEmpties = definition.OrderAroundEmpties ?? loader.orderEmpties;
                loader.orderAwayLoaded = definition.OrderAroundLoaded ?? loader.orderAwayLoaded;
                return;
            }

            var unloader = component as IndustryUnloader;
            if (unloader != null)
            {
                unloader.load = load;
                unloader.storageConsumptionRate = definition.StorageChangeRate ?? unloader.storageConsumptionRate;
                unloader.maxStorage = definition.MaxStorage ?? unloader.maxStorage;
                unloader.carUnloadRate = definition.CarTransferRate ?? unloader.carUnloadRate;
                unloader.orderAwayEmpties = definition.OrderAroundEmpties ?? unloader.orderAwayEmpties;
                unloader.orderLoads = definition.OrderAroundLoaded ?? unloader.orderLoads;
                return;
            }

            var formulaic = component as FormulaicIndustryComponent;
            if (formulaic != null)
            {
                formulaic.inputTerms = BuildFormulaTerms(definition.InputTermsPerDay);
                formulaic.outputTerms = BuildFormulaTerms(definition.OutputTermsPerDay);
                return;
            }

            var repairTrack = component as RepairTrack;
            if (repairTrack != null)
            {
                if (load != null)
                {
                    RepairPartsLoadField?.SetValue(repairTrack, load);
                }

                if (definition.CanOverhaul != null)
                {
                    repairTrack.canOverhaul = definition.CanOverhaul.Value;
                }

                return;
            }

            var teamTrack = component as TeamTrack;
            if (teamTrack != null)
            {
                teamTrack.idealCars = definition.IdealCars ?? teamTrack.idealCars;
                teamTrack.profile = BuildTeamTrackProfile(definition.TeamProfiles);
                return;
            }

            var interchangedLoader = component as InterchangedIndustryLoader;
            if (interchangedLoader != null)
            {
                interchangedLoader.load = load;
                return;
            }

            var fuseInterchangedUnloader = component as FuseInterchangedIndustryUnloader;
            if (fuseInterchangedUnloader != null)
            {
                fuseInterchangedUnloader.load = load;
                return;
            }

            if (TryApplyOptionalType(component, "Model.Ops.InterchangedIndustryUnloader", obj =>
            {
                ApplyOptionalLoadField(obj, load);
            }))
            {
                return;
            }

            if (TryApplyOptionalType(component, "Model.Ops.TeleportLoadingIndustry", obj =>
            {
                ApplyOptionalLoadField(obj, load);
                ApplyTeleportLoadingFields(obj, definition);
            }))
            {
                return;
            }

            if (TryApplyOptionalType(component, "Model.Ops.ProgressionIndustryComponent", obj =>
            {
                FuseLog.Info(
                    $"FUSE applied package='{definition.Type ?? "<unspecified>"}' " +
                    $"operation='industry component apply' kind='progression' " +
                    $"id='{DescribeComponent(component)}' " +
                    "message='progression industry component bound'.");
            }))
            {
                return;
            }

            var interchange = component as Interchange;
            if (interchange != null)
            {
                FuseLog.Info($"FUSE applied generic interchange setup for component '{DescribeComponent(component)}' trackSpanCount={component.trackSpans?.Length ?? 0}.");
                return;
            }

            var passengerStop = component as FusePassengerStopComponent;
            if (passengerStop != null)
            {
                passengerStop.PassengerStopId = definition.PassengerStopId;
                passengerStop.PassengerLoad = load;
                passengerStop.TimetableCode = definition.TimetableCode;
                passengerStop.BasePopulation = definition.BasePopulation ?? passengerStop.BasePopulation;
                passengerStop.NeighborIds = definition.NeighborIds ?? Array.Empty<string>();
                passengerStop.Branch = definition.Branch;
                passengerStop.BranchDefinitions = definition.BranchDefinitions ?? Array.Empty<FusePassengerBranch>();
            }

            ApplyCustomIndustryComponentFields(component, definition, load);

            var appliedComponent = component as IFuseAppliedComponent;
            if (appliedComponent != null)
            {
                appliedComponent.OnFuseDefinitionApplied();
            }
        }

        // Reflection helpers for component types that may be absent in some
        // game versions. They keep the apply / read pipeline tolerant without
        // taking a hard compile-time dependency on every Model.Ops subclass.

        private static bool IsType(object instance, string fullTypeName)
        {
            if (instance == null || string.IsNullOrEmpty(fullTypeName))
            {
                return false;
            }

            var type = TryResolveIndustryComponentType(fullTypeName);
            return type != null && type.IsInstanceOfType(instance);
        }

        private static bool TryApplyOptionalType(IndustryComponent component, string fullTypeName, Action<IndustryComponent> apply)
        {
            if (!IsType(component, fullTypeName))
            {
                return false;
            }

            apply?.Invoke(component);
            return true;
        }

        private static void ApplyOptionalLoadField(IndustryComponent component, Load load)
        {
            var field = component.GetType().GetField("load", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && (load != null || field.GetValue(component) == null))
            {
                field.SetValue(component, load);
            }
        }

        private static void ApplyTeleportLoadingFields(IndustryComponent component, FuseIndustryComponent definition)
        {
            var type = component.GetType();
            type.GetField("inputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .SetValue(component, ResolveSpans(definition.InputSpanIds));
            type.GetField("outputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .SetValue(component, ResolveSpans(definition.OutputSpanIds));
            if (definition.CarLoadPeriod != null)
            {
                type.GetField("carLoadPeriod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .SetValue(component, definition.CarLoadPeriod.Value);
            }

            if (definition.CarLengthFeet != null)
            {
                type.GetField("carLengthFeet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .SetValue(component, definition.CarLengthFeet.Value);
            }

            ApplyIndustryLoaderBaseSharedFields(component, definition);
        }

        private static void ApplyPartialTeleportLoadingFields(IndustryComponent component, FuseIndustryComponent definition)
        {
            var type = component.GetType();
            if (definition.InputSpanIds != null && definition.InputSpanIds.Length > 0)
            {
                var field = type.GetField("inputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var existing = field?.GetValue(component) as TrackSpan[];
                field?.SetValue(component, MergeSpans(existing, ResolveSpans(definition.InputSpanIds)));
            }

            if (definition.OutputSpanIds != null && definition.OutputSpanIds.Length > 0)
            {
                var field = type.GetField("outputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var existing = field?.GetValue(component) as TrackSpan[];
                field?.SetValue(component, MergeSpans(existing, ResolveSpans(definition.OutputSpanIds)));
            }

            if (definition.CarLoadPeriod != null)
            {
                type.GetField("carLoadPeriod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .SetValue(component, definition.CarLoadPeriod.Value);
            }

            if (definition.CarLengthFeet != null)
            {
                type.GetField("carLengthFeet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .SetValue(component, definition.CarLengthFeet.Value);
            }

            ApplyIndustryLoaderBaseSharedFields(component, definition);
        }

        /// <summary>
        /// Applies the fields that <see cref="IndustryLoader"/> and
        /// <see cref="Model.Ops.TeleportLoadingIndustry"/> both inherit
        /// from <c>IndustryLoaderBase</c> — <c>productionRate</c>,
        /// <c>maxStorage</c>, <c>orderEmpties</c> — from the FUSE
        /// definition's <c>StorageChangeRate</c>, <c>MaxStorage</c>, and
        /// <c>OrderAroundEmpties</c>. The IndustryLoader path in
        /// <see cref="ApplyComponentDefinition"/> sets these directly,
        /// but the TeleportLoadingIndustry path goes through
        /// <see cref="TryApplyOptionalType"/> and previously only set
        /// the subclass-specific fields (inputSpans, outputSpans,
        /// carLoadPeriod, carLengthFeet) and the reflective <c>load</c>.
        /// That left <c>maxStorage</c> at its compile-time default of
        /// <c>1f</c>, which silently caps the shared output buffer at
        /// one unit and makes the upstream <see
        /// cref="FormulaicIndustryComponent"/> emit
        /// "Production Stopped: &lt;output load&gt;" the moment storage
        /// fills past the per-tick production amount. Symptomatic
        /// pack: Foxy's Kirkland Coal Patch, which switches
        /// <c>kirkland-mine.coal</c> from <c>IndustryLoader</c> to
        /// <c>TeleportLoadingIndustry</c> and declares
        /// <c>maxStorage: 27000000</c>; without this method the runtime
        /// cap stays at 1.
        /// </summary>
        private static void ApplyIndustryLoaderBaseSharedFields(IndustryComponent component, FuseIndustryComponent definition)
        {
            var loaderBase = component as IndustryLoaderBase;
            if (loaderBase == null)
            {
                return;
            }

            if (definition.MaxStorage != null)
            {
                loaderBase.maxStorage = definition.MaxStorage.Value;
            }

            if (definition.StorageChangeRate != null)
            {
                loaderBase.productionRate = definition.StorageChangeRate.Value;
            }

            if (definition.OrderAroundEmpties != null)
            {
                loaderBase.orderEmpties = definition.OrderAroundEmpties.Value;
            }
        }

        private static void ReadTeleportLoadingFields(IndustryComponent component, FuseIndustryComponent definition)
        {
            var type = component.GetType();
            var inputSpans = type.GetField("inputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(component) as TrackSpan[];
            var outputSpans = type.GetField("outputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(component) as TrackSpan[];

            definition.InputSpanIds = inputSpans?
                .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                .Select(span => span.id)
                .ToArray();
            definition.OutputSpanIds = outputSpans?
                .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                .Select(span => span.id)
                .ToArray();

            var carLoadPeriod = type.GetField("carLoadPeriod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (carLoadPeriod != null)
            {
                definition.CarLoadPeriod = (float)carLoadPeriod.GetValue(component);
            }

            var carLengthFeet = type.GetField("carLengthFeet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (carLengthFeet != null)
            {
                definition.CarLengthFeet = (float)carLengthFeet.GetValue(component);
            }
        }

        private static void ApplyCustomIndustryComponentFields(IndustryComponent component, FuseIndustryComponent definition, Load load)
        {
            if (component == null || definition == null)
            {
                return;
            }

            var typeName = component.GetType().FullName;
            if (FuseIndustryComponentTypes.IsKnown(definition.Type))
            {
                return;
            }

            SetLoadField(component, "load", load);
            SetLoadField(component, "convertedLoad", ResolveLoad(definition.ConvertedLoadId));
            SetFloatField(component, "carLoadRate", definition.CarTransferRate);
            SetFloatField(component, "carUnloadRate", definition.CarTransferRate);
            SetFloatField(component, "loadRate", definition.CarTransferRate);
            SetFloatField(component, "maxStorage", definition.MaxStorage);
            SetFloatField(component, "costPerUnit", definition.CostPerUnit);
            SetFloatField(component, "notBefore", definition.NotBeforeHour);
            SetFloatField(component, "notAfter", definition.NotAfterHour);
            SetFloatField(component, "fillPercentage", definition.FillPercentage);
            SetStringField(component, "title", definition.Title ?? definition.Name);
            SetStringArrayField(component, "bookReasons", definition.BookReasons);
            ApplyCustomFieldBag(component, definition.Fields);

            FuseLog.Info(
                $"FUSE applied reflective custom industry component fields type='{typeName}' " +
                $"id='{DescribeComponent(component)}' loadId='{definition.LoadId ?? string.Empty}' " +
                $"convertedLoadId='{definition.ConvertedLoadId ?? string.Empty}'.");
        }
    }
}
