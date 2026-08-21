using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FUSE.Authoring.Data;
using FUSE.Compatibility;
using FUSE.Patches;
using FUSE.Runtime.API;
using Game.Messages;
using Game.Progression;
using HarmonyLib;
using KeyValue.Runtime;
using Model.Ops;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Track;
using UI.Builder;
using UI.CarInspector;
using UnityEngine;
using UnityEngine.Rendering;

namespace FUSE.UnityTests
{
    public sealed class LegacyRuntimeBehaviorTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("FUSE legacy runtime behavior tests");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void ConfusingSupplementsEmpty_RemainsVisibleAndAcceptsEveryAutoDestinationKind()
        {
            var component = _root.AddComponent<FuseLegacyPlaceholderIndustryComponent>();
            component.trackSpans = new TrackSpan[1];

            Assert.That(component.IsVisible, Is.True);
            Assert.That(
                Enum.GetValues(typeof(AutoDestinationType)).Cast<AutoDestinationType>()
                    .All(component.WantsAutoDestination),
                Is.True);
        }

        [TestCase("TurntableMeasurementTool")]
        [TestCase("scenery://TurntableMeasurementTool")]
        [TestCase("ALW_ModRes_TurntableMeasurementTool")]
        public void TurntableMeasurementPlate_IsEditorOnly(string identifier)
        {
            Assert.That(SceneryAPI.IsEditorOnlyLegacySceneryReference(new FuseScenery
            {
                AssetIdentifier = identifier
            }), Is.True);
        }

        [Test]
        public void OrdinaryTurntableScenery_IsNotEditorOnly()
        {
            Assert.That(SceneryAPI.IsEditorOnlyLegacySceneryReference(new FuseScenery
            {
                AssetIdentifier = "scenery://ALW_ModRes_plate50x250"
            }), Is.False);
        }

        [Test]
        public void ObjectLineUniformSpacing_IncludesFinalEndpoint()
        {
            var placements = FuseObjectLineLayout.Build(
                new[] { Point(0f, 0f), Point(10f, 0f) },
                4f,
                true,
                10);

            Assert.That(placements.Select(value => value.Position.x),
                Is.EqualTo(new[] { 0f, 4f, 8f, 10f }).Within(0.001f));
            Assert.That(placements.All(value => value.Forward == Vector3.right), Is.True);
        }

        [Test]
        public void ObjectLineSpacing_ContinuesAcrossPolylineCorners()
        {
            var placements = FuseObjectLineLayout.Build(
                new[] { Point(0f, 0f), Point(5f, 0f), Point(5f, 5f) },
                4f,
                true,
                10);

            Assert.That(placements.Count, Is.EqualTo(4));
            Assert.That(placements[2].Position, Is.EqualTo(new Vector3(5f, 0f, 3f)));
            Assert.That(placements[2].Forward, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void ObjectLineSafetyLimit_RejectsInstanceExplosion()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                FuseObjectLineLayout.Build(
                    new[] { Point(0f, 0f), Point(100f, 0f) },
                    1f,
                    true,
                    20));

            StringAssert.Contains("20 instance safety limit", exception.Message);
        }

        [Test]
        public void ObjectLineDuplicateOnlyPath_IsRejected()
        {
            Assert.Throws<InvalidOperationException>(() =>
                FuseObjectLineLayout.Build(
                    new[] { Point(2f, 3f), Point(2f, 3f) },
                    5f,
                    true,
                    20));
        }

        [Test]
        public void SourceLakeProfile_CopiesRenderingFlowAndTerrainControls()
        {
            var source = _root.AddComponent<LakePolygon>();
            source.distSmooth = 7.5f;
            source.terrainSmoothMultiplier = 2.5f;
            source.overrideLakeRender = true;
            source.receiveShadows = true;
            source.shadowCastingMode = ShadowCastingMode.TwoSided;
            source.automaticFlowMapScale = 0.65f;
            source.noiseflowMap = true;
            source.noiseMultiplierflowMap = 1.7f;
            source.noiseSizeXflowMap = 0.15f;
            source.noiseSizeZflowMap = 0.35f;
            source.floatSpeed = 12f;
            source.flowSpeed = 3f;
            source.flowDirection = 42f;
            source.normalFromRaycast = true;
            source.snapMask = 123;

            var targetObject = new GameObject("target lake");
            targetObject.transform.SetParent(_root.transform, false);
            var target = targetObject.AddComponent<LakePolygon>();
            InvokeApplySourceProfile(source, target);

            Assert.That(target.distSmooth, Is.EqualTo(source.distSmooth));
            Assert.That(target.terrainSmoothMultiplier, Is.EqualTo(source.terrainSmoothMultiplier));
            Assert.That(target.overrideLakeRender, Is.EqualTo(source.overrideLakeRender));
            Assert.That(target.receiveShadows, Is.EqualTo(source.receiveShadows));
            Assert.That(target.shadowCastingMode, Is.EqualTo(source.shadowCastingMode));
            Assert.That(target.automaticFlowMapScale, Is.EqualTo(source.automaticFlowMapScale));
            Assert.That(target.noiseflowMap, Is.EqualTo(source.noiseflowMap));
            Assert.That(target.noiseMultiplierflowMap, Is.EqualTo(source.noiseMultiplierflowMap));
            Assert.That(target.noiseSizeXflowMap, Is.EqualTo(source.noiseSizeXflowMap));
            Assert.That(target.noiseSizeZflowMap, Is.EqualTo(source.noiseSizeZflowMap));
            Assert.That(target.floatSpeed, Is.EqualTo(source.floatSpeed));
            Assert.That(target.flowSpeed, Is.EqualTo(source.flowSpeed));
            Assert.That(target.flowDirection, Is.EqualTo(source.flowDirection));
            Assert.That(target.normalFromRaycast, Is.EqualTo(source.normalFromRaycast));
            Assert.That(target.snapMask.value, Is.EqualTo(source.snapMask.value));
        }

        [Test]
        public void MissingSourceLake_UsesNonShadowedSafeDefaults()
        {
            var target = _root.AddComponent<LakePolygon>();
            target.receiveShadows = true;
            target.shadowCastingMode = ShadowCastingMode.TwoSided;

            InvokeApplySourceProfile(null, target);

            Assert.That(target.currentProfile, Is.Null);
            Assert.That(target.receiveShadows, Is.False);
            Assert.That(target.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
        }

        [Test]
        public void StrangeCustomsFileCache_NormalizesPathsAndTracksEntryState()
        {
            var relative = Path.Combine("fixtures", "audio.wav");
            var normalized = StrangeCustoms.FileCache.NormalizePath(relative);
            var equivalent = StrangeCustoms.FileCache.NormalizePath(
                Path.Combine("fixtures", ".", "audio.wav"));

            Assert.That(Path.IsPathRooted(normalized), Is.True);
            Assert.That(equivalent, Is.EqualTo(normalized));

            var entry = new StrangeCustoms.FileCache.CacheEntry<string>(relative);
            string observed = null;
            entry.Register(value => observed = value);
            entry.Set("loaded");

            Assert.That(entry.IsValid, Is.True);
            Assert.That(entry.IsLoading, Is.False);
            Assert.That(entry.Value, Is.EqualTo("loaded"));
            Assert.That(observed, Is.EqualTo("loaded"));

            entry.Invalidate();
            Assert.That(entry.IsValid, Is.False);
            Assert.That(entry.Value, Is.Null);
        }

        [Test]
        public void StrangeCustomsFlowyBuilder_AdaptsLegacyDataToNativeSpliney()
        {
            var definition = StrangeCustoms.FlowyThingBuilder.ConvertDefinition(
                JObject.Parse(@"{
                    'handler': 'StrangeCustoms.FlowyThingBuilder',
                    'style': 'River',
                    'profile': 'River profile',
                    'points': [
                        { 'position': { 'x': 1, 'y': 2, 'z': 3 }, 'width': 4 },
                        { 'position': { 'x': 5, 'y': 6, 'z': 7 }, 'width': 8 }
                    ]
                }"));

            Assert.That(definition.Type, Is.EqualTo("river"));
            Assert.That(definition.Profile, Is.EqualTo("River profile"));
            Assert.That(definition.OffsetY, Is.EqualTo(-0.1f));
            Assert.That(definition.Points.Length, Is.EqualTo(2));
            Assert.That(definition.Points[0].Width, Is.EqualTo(4f));
            Assert.That(definition.Points[1].Position.z, Is.EqualTo(7f));
        }

        [Test]
        public void LabelPrinter_SavedPropertyIdTrimsGroupThenNameFallback()
        {
            var grouped = new FuseConfusingSupplementsLabelPrinterComponent
            {
                Name = "Visible Name",
                Group = " shared-label "
            };
            var ungrouped = new FuseConfusingSupplementsLabelPrinterComponent
            {
                Name = " road-name ",
                Group = "   "
            };

            Assert.That(grouped.SavedPropertyId, Is.EqualTo("shared-label"));
            Assert.That(ungrouped.SavedPropertyId, Is.EqualTo("road-name"));
            Assert.That(
                FuseConfusingSupplementsLabelPrinterBuilder.SavedPropertyKey(ungrouped.SavedPropertyId),
                Is.EqualTo("cs.labelprinter.road-name"));
            Assert.That(FuseConfusingSupplementsLabelPrinterBuilder.ReadText(Value.Null()), Is.Empty);
        }

        [Test]
        public void LabelPrinter_UpdatedTextPreservesDictionaryFields()
        {
            var current = Value.Dictionary(new Dictionary<string, Value>
            {
                ["text"] = Value.String("Old text"),
                ["font"] = Value.String("Railroad Roman")
            });

            var updated = FuseConfusingSupplementsLabelPrinterBuilder.UpdatedTextValue(current, "New text");
            var runtime = PropertyValueConverter.SnapshotToRuntime(updated);

            Assert.That(FuseConfusingSupplementsLabelPrinterBuilder.ReadText(runtime), Is.EqualTo("New text"));
            Assert.That(runtime.DictionaryValue["font"].StringValue, Is.EqualTo("Railroad Roman"));
        }

        [Test]
        public void LiveryController_BlankOrMissingSelectionRestoresOriginalTexture()
        {
            var controller = CreateLiveryController(
                out var material,
                out var originalTexture,
                out var replacementTexture);

            try
            {
                material.mainTexture = replacementTexture;
                controller.ApplySavedSelection(Value.Null());
                Assert.That(material.mainTexture, Is.SameAs(originalTexture));

                material.mainTexture = replacementTexture;
                controller.ApplySavedSelection(Value.String("removed-livery"));
                Assert.That(material.mainTexture, Is.SameAs(originalTexture));
            }
            finally
            {
                FuseConfusingSupplementsLiveryRegistry.Shutdown();
                UnityEngine.Object.DestroyImmediate(originalTexture);
                UnityEngine.Object.DestroyImmediate(replacementTexture);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void LiveryRegistry_RefreshLiveCarsRestoresOriginalBeforeClearingCache()
        {
            CreateLiveryController(
                out var material,
                out var originalTexture,
                out var replacementTexture);

            try
            {
                material.mainTexture = replacementTexture;
                AddCachedLiveryTexture("refresh-test", replacementTexture);

                var refreshed = FuseConfusingSupplementsLiveryRegistry.RefreshLiveCars();

                Assert.That(refreshed, Is.GreaterThanOrEqualTo(1));
                Assert.That(material.mainTexture, Is.SameAs(originalTexture));
                Assert.That(FuseConfusingSupplementsLiveryRegistry.CachedTextureCount, Is.Zero);
            }
            finally
            {
                FuseConfusingSupplementsLiveryRegistry.Shutdown();
                UnityEngine.Object.DestroyImmediate(originalTexture);
                if (replacementTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(replacementTexture);
                }

                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void LiveryController_OnDestroyDestroysEveryTrackedMaterialOnce()
        {
            var shader = Shader.Find("Unlit/Texture") ??
                         Shader.Find("Sprites/Default") ??
                         Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);

            var controllerObject = new GameObject("livery material cleanup fixture");
            controllerObject.transform.SetParent(_root.transform, false);
            var controller = controllerObject.AddComponent<FuseConfusingSupplementsLiveryController>();
            var sourceTexture = new Texture2D(1, 1);
            var sourceMaterial = new Material(shader);
            sourceMaterial.mainTexture = sourceTexture;
            try
            {
                var firstRendererObject = new GameObject("first livery renderer");
                firstRendererObject.transform.SetParent(controllerObject.transform, false);
                var firstRenderer = firstRendererObject.AddComponent<MeshRenderer>();
                firstRenderer.sharedMaterial = sourceMaterial;

                var secondRendererObject = new GameObject("second livery renderer");
                secondRendererObject.transform.SetParent(controllerObject.transform, false);
                var secondRenderer = secondRendererObject.AddComponent<MeshRenderer>();
                secondRenderer.sharedMaterial = sourceMaterial;

                controller.CaptureMaterials(controllerObject);
                var capturedMaterials = new[]
                {
                    firstRenderer.material,
                    secondRenderer.material
                };

                var ownedMaterialsField = typeof(FuseConfusingSupplementsLiveryController).GetField(
                    "_ownedMaterials",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(ownedMaterialsField, Is.Not.Null);
                var ownedMaterials = ownedMaterialsField.GetValue(controller) as ISet<Material>;
                Assert.That(ownedMaterials, Is.Not.Null);
                Assert.That(ownedMaterials.Count, Is.EqualTo(capturedMaterials.Distinct().Count()));

                var materialSnapshotsField = typeof(FuseConfusingSupplementsLiveryController).GetField(
                    "_materials",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(materialSnapshotsField, Is.Not.Null);
                var materialSnapshots = materialSnapshotsField.GetValue(controller) as ICollection;
                Assert.That(materialSnapshots, Is.Not.Null);
                var initialSnapshotCount = materialSnapshots.Count;
                Assert.That(initialSnapshotCount, Is.GreaterThan(0));

                controller.CaptureMaterials(controllerObject);
                Assert.That(ownedMaterials.Count, Is.EqualTo(capturedMaterials.Distinct().Count()));
                Assert.That(materialSnapshots.Count, Is.EqualTo(initialSnapshotCount));

                UnityEngine.Object.DestroyImmediate(controllerObject);

                Assert.That(capturedMaterials.All(material => material == null), Is.True);
                Assert.That(sourceMaterial == null, Is.False);
            }
            finally
            {
                if (controllerObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(controllerObject);
                }

                if (sourceMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(sourceMaterial);
                }

                if (sourceTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(sourceTexture);
                }
            }
        }

        [Test]
        public void LiveryRegistry_DestroyTextureUsesImmediateCleanupInEditMode()
        {
            Assert.That(Application.isPlaying, Is.False);
            var texture = new Texture2D(1, 1);

            FuseConfusingSupplementsLiveryRegistry.DestroyTexture(texture);

            Assert.That(texture == null, Is.True);
        }

        [Test]
        public void LiveryRegistry_FileIndexIsDeterministicAndClearedWithTextureCache()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FUSE-livery-index-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var jpgPath = Path.Combine(directory, "boiler.jpg");
                var pngPath = Path.Combine(directory, "boiler.png");
                File.WriteAllBytes(pngPath, Array.Empty<byte>());
                File.WriteAllBytes(jpgPath, Array.Empty<byte>());

                var first = FuseConfusingSupplementsLiveryRegistry.GetTextureFiles(directory);
                Assert.That(first["boiler"], Is.EqualTo(jpgPath));

                var cabPath = Path.Combine(directory, "cab.png");
                File.WriteAllBytes(cabPath, Array.Empty<byte>());
                var cached = FuseConfusingSupplementsLiveryRegistry.GetTextureFiles(directory);
                Assert.That(cached, Is.SameAs(first));
                Assert.That(cached.ContainsKey("cab"), Is.False);

                FuseConfusingSupplementsLiveryRegistry.Shutdown();
                var refreshed = FuseConfusingSupplementsLiveryRegistry.GetTextureFiles(directory);
                Assert.That(refreshed.ContainsKey("cab"), Is.True);
            }
            finally
            {
                FuseConfusingSupplementsLiveryRegistry.Shutdown();
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void GracePatch_TargetsTheLocationOverload()
        {
            var expected = AccessTools.Method(
                typeof(OpsController),
                "CalculateGraceDays",
                new[] { typeof(Location), typeof(Location) });
            var declared = ResolveDeclaredHarmonyTarget(typeof(FuseFallFromGraceCalculationPatch));

            Assert.That(expected, Is.Not.Null);
            Assert.That(declared, Is.EqualTo(expected));
        }

        [Test]
        public void InspectorPatch_TargetsTheWaybillPanelOverload()
        {
            var expected = AccessTools.Method(
                typeof(CarInspector),
                "PopulateWaybillPanel",
                new[] { typeof(UIPanelBuilder), typeof(Waybill) });
            var declared = ResolveDeclaredHarmonyTarget(typeof(FuseFallFromGraceInspectorPatch));

            Assert.That(expected, Is.Not.Null);
            Assert.That(declared, Is.EqualTo(expected));
        }

        private static MethodInfo ResolveDeclaredHarmonyTarget(Type patchType)
        {
            Type declaringType = null;
            string methodName = null;
            Type[] argumentTypes = null;
            foreach (var patch in patchType.GetCustomAttributes(typeof(HarmonyPatch), false).Cast<HarmonyPatch>())
            {
                if (patch.info?.declaringType != null)
                {
                    declaringType = patch.info.declaringType;
                }

                if (!string.IsNullOrWhiteSpace(patch.info?.methodName))
                {
                    methodName = patch.info.methodName;
                }

                if (patch.info?.argumentTypes != null)
                {
                    argumentTypes = patch.info.argumentTypes;
                }
            }

            Assert.That(declaringType, Is.Not.Null);
            Assert.That(methodName, Is.Not.Null.And.Not.Empty);
            return AccessTools.Method(declaringType, methodName, argumentTypes);
        }

        [TestCase(0f, 0)]
        [TestCase(0.24f, 0)]
        [TestCase(0.25f, 0)]
        [TestCase(0.26f, 1)]
        [TestCase(0.99f, 1)]
        public void WeightedSelection_UsesRemainingCapacity(float sample, int expected)
        {
            Assert.That(
                FuseOutboundIndustryRoutingPatch.SelectWeightedIndex(
                    new[] { 1f, 3f },
                    sample),
                Is.EqualTo(expected));
        }

        [Test]
        public void WeightedSelection_HandlesEmptyAndZeroWeightSets()
        {
            Assert.That(
                FuseOutboundIndustryRoutingPatch.SelectWeightedIndex(
                    Array.Empty<float>(),
                    0.5f),
                Is.EqualTo(-1));
            Assert.That(
                FuseOutboundIndustryRoutingPatch.SelectWeightedIndex(
                    new[] { 0f, 0f },
                    0.75f),
                Is.EqualTo(1));
        }

        [Test]
        public void CompanyStarterQueue_SkipsNullPlacements()
        {
            var validPlacement = new SetupDescriptor.CarPlacement
            {
                carIdentifier = new[] { "starter-car" }
            };
            var setup = _root.AddComponent<SetupDescriptor>();
            setup.identifier = "ewh-company";
            setup.placements = new[] { null, validPlacement };
            var queue = new Queue<SetupDescriptor.CarPlacement>();

            var queued = FuseCompanyStarterPlacementPatch.QueueStarterPlacements(
                setup,
                queue);

            Assert.That(queued, Is.EqualTo(1));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.Peek(), Is.SameAs(validPlacement));
            Assert.That(setup.placements, Is.Empty);
        }

        private static FuseSplineyPoint Point(float x, float z)
        {
            return new FuseSplineyPoint { Position = new Vector3(x, 0f, z) };
        }

        private static void InvokeApplySourceProfile(LakePolygon source, LakePolygon target)
        {
            var method = typeof(WaterSurfaceAPI).GetMethod(
                "ApplySourceLakeProfile",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { source, target });
        }

        private FuseConfusingSupplementsLiveryController CreateLiveryController(
            out Material material,
            out Texture2D originalTexture,
            out Texture2D replacementTexture)
        {
            var shader = Shader.Find("Unlit/Texture") ??
                         Shader.Find("Sprites/Default") ??
                         Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);

            var carObject = new GameObject("livery controller fixture");
            carObject.transform.SetParent(_root.transform, false);
            var renderer = carObject.AddComponent<MeshRenderer>();
            material = new Material(shader);
            originalTexture = new Texture2D(1, 1) { name = "original" };
            replacementTexture = new Texture2D(1, 1) { name = "replacement" };
            material.mainTexture = originalTexture;
            renderer.sharedMaterial = material;

            var propertyIds = new List<int>();
            material.GetTexturePropertyNameIDs(propertyIds);
            var fixtureMaterial = material;
            var fixtureOriginalTexture = originalTexture;
            var propertyId = propertyIds.First(
                id => fixtureMaterial.GetTexture(id) == fixtureOriginalTexture);

            var controller = carObject.AddComponent<FuseConfusingSupplementsLiveryController>();
            var controllerType = typeof(FuseConfusingSupplementsLiveryController);
            var snapshotType = controllerType.GetNestedType("MaterialSnapshot", BindingFlags.NonPublic);
            Assert.That(snapshotType, Is.Not.Null);
            var constructor = snapshotType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(Material), typeof(int), typeof(Texture), typeof(string) },
                null);
            Assert.That(constructor, Is.Not.Null);

            var materialsField = controllerType.GetField("_materials", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(materialsField, Is.Not.Null);
            var snapshots = materialsField.GetValue(controller) as IList;
            Assert.That(snapshots, Is.Not.Null);
            snapshots.Add(constructor.Invoke(new object[]
            {
                material,
                propertyId,
                originalTexture,
                originalTexture.name
            }));

            SetPrivateField(controller, "_carIdentifier", "fuse-unity-test-" + Guid.NewGuid());
            SetPrivateField(controller, "_configured", true);
            return controller;
        }

        private static void AddCachedLiveryTexture(string key, Texture2D texture)
        {
            var field = typeof(FuseConfusingSupplementsLiveryRegistry).GetField(
                "Textures",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var textures = field.GetValue(null) as IDictionary;
            Assert.That(textures, Is.Not.Null);
            textures[key] = texture;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
