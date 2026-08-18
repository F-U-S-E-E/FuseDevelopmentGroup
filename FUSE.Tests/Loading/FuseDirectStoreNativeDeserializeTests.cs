using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUSE.Loading;
using HarmonyLib;
using Model.Definition;
using Model.Definition.Components;
using Model.Definition.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// In-process proof of the direct-store cold-load contract against the REAL game
    /// assemblies (Definition.dll's <see cref="ContainerSerialization"/>, its Vec3Conv,
    /// JsonSubtypes binding of <see cref="Component"/> kinds) and a REAL Harmony runtime.
    ///
    /// Background: FUSE mounts mod asset packs as fuseasset:// direct stores whose
    /// <c>AssetPackRuntimeStore.Container()</c> body is replaced by a Harmony prefix, so the
    /// game's own call to <c>ContainerSerialization.Deserialize</c> never runs for them.
    /// Old-loader mods (LegosLibraryOfStuff) hang a Harmony POSTFIX on that public method to
    /// inject clone definitions (repaint liveries, LLW tender swaps). If FUSE's cold load
    /// does not go through the public entry point, those clones never exist for any
    /// mod-pack car (issues #224 / #222). These tests install an LLoS-shaped postfix and
    /// drive FUSE's real private cold-load and re-deserialize helpers through reflection.
    /// </summary>
    public sealed class FuseDirectStoreNativeDeserializeTests : IDisposable
    {
        // Unique identifiers so the postfix ignores any Deserialize call issued by another
        // test class that happens to run concurrently in the same process.
        private const string BaseIdentifier = "fuse-test-native-deser-boxcar";
        private const string CloneSuffix = "-clone-test";
        private const string CloneIdentifier = BaseIdentifier + CloneSuffix;
        private const string StoreIdentifier = "fuseasset://fuse-test-native-deser-pack";

        // Recorded per-call: the identifiers the container carried when the postfix saw it.
        // Only calls that contain BaseIdentifier are counted (see PostfixCallCount).
        private static readonly List<string[]> RecordedCalls = new List<string[]>();
        private static readonly object RecordLock = new object();

        private static readonly MethodInfo GameSettingsMethod =
            AccessTools.Method(typeof(ContainerSerialization), "JsonSerializerSettings");

        private static readonly MethodInfo LoadResilientDirectContainerMethod =
            typeof(FuseAssetPackRegistry).GetMethod(
                "LoadResilientDirectContainer",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(string), typeof(IDictionary<string, int>) },
                null);

        private static readonly MethodInfo BypassDeserializeMethod =
            typeof(FuseAssetPackRegistry).GetMethod(
                "BypassDeserialize",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

        private static readonly MethodInfo MixintoDeserializeItemMethod =
            typeof(FuseLegacyContainerMixintoRegistry).GetMethod(
                "DeserializeItem",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(JObject) },
                null);

        private readonly Harmony _harmony;
        private readonly MethodInfo _target;

        public FuseDirectStoreNativeDeserializeTests()
        {
            lock (RecordLock)
            {
                RecordedCalls.Clear();
            }

            _target = AccessTools.Method(typeof(ContainerSerialization), nameof(ContainerSerialization.Deserialize), new[] { typeof(string) });
            Assert.NotNull(_target);

            _harmony = new Harmony("fuse.tests.direct-store-native-deserialize." + Guid.NewGuid().ToString("N"));
            _harmony.Patch(
                _target,
                postfix: new HarmonyMethod(typeof(FuseDirectStoreNativeDeserializeTests), nameof(LlosLikePostfix)));
        }

        public void Dispose()
        {
            _harmony.Unpatch(_target, HarmonyPatchType.Postfix, _harmony.Id);
        }

        // ------------------------------------------------------------------
        // The LLoS-shaped postfix.
        // Mirrors LegosLibraryOfStuff.ContainerSerializationDeserializePatch.Postfix: it
        // walks __result.Objects, and for a matching identifier appends a clone produced by
        // serialize -> deserialize with the game's own container settings (LLoS's CloneItem
        // does exactly that with an identical settings factory), then re-identifies it.
        // ------------------------------------------------------------------
        private static void LlosLikePostfix(ref Container __result)
        {
            var identifiers = __result?.Objects?.Select(o => o?.Identifier).ToArray() ?? Array.Empty<string>();
            lock (RecordLock)
            {
                RecordedCalls.Add(identifiers);
            }

            if (__result?.Objects == null)
            {
                return;
            }

            var toAdd = new List<ContainerItem>();
            foreach (var item in __result.Objects)
            {
                if (item?.Identifier != BaseIdentifier)
                {
                    continue;
                }

                var settings = (JsonSerializerSettings)GameSettingsMethod.Invoke(null, null);
                var clone = JsonConvert.DeserializeObject<ContainerItem>(
                    JsonConvert.SerializeObject(item, settings), settings);
                clone.Identifier = item.Identifier + CloneSuffix;
                if (clone.Metadata != null)
                {
                    clone.Metadata.Name = (item.Metadata?.Name ?? string.Empty) + " (clone)";
                }

                toAdd.Add(clone);
            }

            __result.Objects.AddRange(toAdd);
        }

        private static int PostfixCallCount
        {
            get
            {
                lock (RecordLock)
                {
                    return RecordedCalls.Count(ids => ids.Contains(BaseIdentifier));
                }
            }
        }

        // ------------------------------------------------------------------
        // Definitions.json fixtures. Trimmed from a real base pack
        // ("PS-1 40ft Boxcar Series/PS-1-40ft-6ft-youngstown-door/Definitions.json"):
        // Car definition with array-shaped Unity structs, PrefabControl + Colorizer
        // components, load slots, metadata.
        // ------------------------------------------------------------------
        private static JObject BoxcarObject(JToken airHosePosition, string extraComponentKind = null)
        {
            var components = new JArray
            {
                new JObject
                {
                    ["kind"] = "PrefabControl",
                    ["prefab"] = "HandbrakeWheel",
                    ["name"] = "Handbrake",
                    ["transform"] = new JObject
                    {
                        ["position"] = new JArray(0.473255, 4.10675049, 6.32707739),
                        ["rotation"] = new JArray(0.0, 0.7071068, 0.0, -0.7071068),
                        ["scale"] = new JArray(0.75, 0.75, 0.75),
                    },
                    ["parent"] = null,
                    ["enabled"] = true,
                },
                new JObject
                {
                    ["kind"] = "Colorizer",
                    ["hexColors"] = new JArray("#4A1F18", "#6e2828"),
                    ["material"] = new JObject { ["materialName"] = "PS-1 Sides" },
                    ["name"] = "Colorable Sides",
                    ["transform"] = new JObject
                    {
                        ["position"] = new JArray(0.0, 0.0, 0.0),
                        ["rotation"] = new JArray(0.0, 0.0, 0.0, 1.0),
                        ["scale"] = new JArray(1.0, 1.0, 1.0),
                    },
                    ["parent"] = null,
                    ["enabled"] = true,
                },
            };

            if (extraComponentKind != null)
            {
                components.Add(new JObject
                {
                    ["kind"] = extraComponentKind,
                    ["name"] = extraComponentKind + " instance",
                    ["enabled"] = true,
                });
            }

            return new JObject
            {
                ["identifier"] = BaseIdentifier,
                ["metadata"] = new JObject
                {
                    ["name"] = "PS-1 40ft 6ft Youngstown Door Boxcar (test)",
                    ["description"] = "trimmed test fixture",
                    ["tags"] = new JArray("Boxcar"),
                    ["credits"] = "Route of the Whippet",
                },
                ["definition"] = new JObject
                {
                    ["kind"] = "Car",
                    ["modelIdentifier"] = "PS-1-40ft-6ft-youngstown-door",
                    ["carType"] = "XM",
                    ["archetype"] = "Boxcar",
                    ["visibleInPlacer"] = true,
                    ["basePrice"] = 1050,
                    ["baseRoadNumber"] = "76000",
                    ["weightEmpty"] = 46800,
                    ["truckIdentifier"] = "truck.asf-a3b",
                    ["loadSlots"] = new JArray(new JObject
                    {
                        ["maximumCapacity"] = 100000.0,
                        ["loadUnits"] = "Pounds",
                        ["requiredLoadIdentifier"] = "",
                    }),
                    ["truckSeparation"] = 9.5,
                    ["length"] = 12.5,
                    ["couplerHeight"] = 0.88,
                    ["airHosePosition"] = airHosePosition,
                    ["brakeAnimations"] = new JArray(),
                    ["minimumCurveRadius"] = "ExtraSmall",
                    ["components"] = components,
                },
            };
        }

        private static string Definitions(JObject singleObject)
        {
            return new JObject { ["objects"] = new JArray(singleObject) }.ToString(Formatting.Indented);
        }

        private static readonly JArray ArrayAirHose = new JArray(-0.37, 0.934, 0.09);

        private static Container ColdLoad(string text, IDictionary<string, int> dropped = null)
        {
            Assert.NotNull(LoadResilientDirectContainerMethod);
            dropped = dropped ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                return (Container)LoadResilientDirectContainerMethod.Invoke(null, new object[] { text, StoreIdentifier, dropped });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        // ------------------------------------------------------------------
        // (1) Cold load of a well-formed pack goes through the public entry point exactly
        //     once, and the old-loader postfix's clone is present in what FUSE returns.
        // ------------------------------------------------------------------
        [Fact]
        public void ColdLoad_WellFormedPack_FiresPublicDeserializePostfixOnce_AndKeepsInjectedClone()
        {
            var text = Definitions(BoxcarObject(ArrayAirHose));

            var container = ColdLoad(text);

            Assert.NotNull(container);
            Assert.Equal(1, PostfixCallCount);

            var identifiers = container.Objects.Select(o => o.Identifier).ToArray();
            Assert.Equal(new[] { BaseIdentifier, CloneIdentifier }, identifiers);

            // The base item bound through the game's real serializer: Car definition,
            // JsonSubtypes-resolved components, array-shaped Vector3 via Vec3Conv.
            var car = Assert.IsType<CarDefinition>(container.Objects[0].Definition);
            Assert.Equal("PS-1-40ft-6ft-youngstown-door", car.ModelIdentifier);
            Assert.Equal(new Vector3(-0.37f, 0.934f, 0.09f), car.AirHosePosition);
            Assert.Collection(car.Components,
                c => Assert.IsType<PrefabControlComponent>(c),
                c => Assert.IsType<ColorizerComponent>(c));

            // The clone is a deep copy carrying the same definition shape.
            var clone = container.Objects[1];
            var cloneCar = Assert.IsType<CarDefinition>(clone.Definition);
            Assert.NotSame(car, cloneCar);
            Assert.Equal(car.ModelIdentifier, cloneCar.ModelIdentifier);
            Assert.Equal(car.AirHosePosition, cloneCar.AirHosePosition);
            Assert.Equal(2, cloneCar.Components.Count);
            Assert.EndsWith(" (clone)", clone.Metadata.Name, StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------
        // (1b) Two different packs each get their own single postfix pass — a pack is never
        //      re-run through the public method during its own cold load.
        // ------------------------------------------------------------------
        [Fact]
        public void ColdLoad_TwoPacks_EachFiresPostfixExactlyOnce()
        {
            var text = Definitions(BoxcarObject(ArrayAirHose));

            ColdLoad(text);
            ColdLoad(text);

            Assert.Equal(2, PostfixCallCount);
        }

        // ------------------------------------------------------------------
        // (2) A pack the stock serializer rejects (object-shaped Vector3: Vec3Conv does
        //     JsonConvert.DeserializeObject<float[]>(token.ToString()) which throws on
        //     {"x":..}) still loads via the tolerant bypass; because the native call threw,
        //     the postfix never ran for it, so no clone exists for that pack.
        // ------------------------------------------------------------------
        [Fact]
        public void ColdLoad_ObjectShapedVector3_NativeRejects_TolerantFallbackLoads_PostfixNotFired()
        {
            var objectAirHose = new JObject { ["x"] = -0.37, ["y"] = 0.934, ["z"] = 0.09 };
            var text = Definitions(BoxcarObject(objectAirHose));

            // Sanity: prove the premise directly against the game's serializer.
            var nativeThrow = Record.Exception(() => ContainerSerialization.Deserialize(text));
            Assert.NotNull(nativeThrow);
            Assert.IsAssignableFrom<JsonException>(nativeThrow.GetBaseException());
            Assert.Equal(0, PostfixCallCount); // original threw => Harmony ran no postfix

            var container = ColdLoad(text);

            Assert.NotNull(container);
            Assert.Equal(0, PostfixCallCount);
            var item = Assert.Single(container.Objects);
            Assert.Equal(BaseIdentifier, item.Identifier);
            var car = Assert.IsType<CarDefinition>(item.Definition);
            // FUSE's TolerantUnityStructConverter read the object-shaped struct.
            Assert.Equal(new Vector3(-0.37f, 0.934f, 0.09f), car.AirHosePosition);
            Assert.Equal(2, car.Components.Count);
        }

        // ------------------------------------------------------------------
        // (2b) An unbindable component kind (its library mod is absent) makes the
        //      stock serializer throw. FUSE drops ONLY that component and then
        //      re-runs the FILTERED text through the public entry point, so the
        //      pack still gets its old-loader edits: postfix fires exactly once
        //      (on the filtered text) and the clone is present. The first native
        //      attempt threw before returning, so that call ran no postfix — the
        //      total is still one pass, never two.
        // ------------------------------------------------------------------
        [Fact]
        public void ColdLoad_UnbindableComponentKind_DropsOnlyThatComponent_ThenFiresPostfixOnceOnFilteredText()
        {
            var text = Definitions(BoxcarObject(ArrayAirHose, extraComponentKind: "FuseTestBogusComponentKind"));
            var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var nativeThrow = Record.Exception(() => ContainerSerialization.Deserialize(text));
            Assert.NotNull(nativeThrow);
            Assert.Equal(0, PostfixCallCount);

            var container = ColdLoad(text, dropped);

            Assert.NotNull(container);
            Assert.Equal(1, PostfixCallCount);
            var identifiers = container.Objects.Select(o => o.Identifier).ToArray();
            Assert.Equal(new[] { BaseIdentifier, CloneIdentifier }, identifiers);
            var car = Assert.IsType<CarDefinition>(container.Objects[0].Definition);
            Assert.Collection(car.Components,
                c => Assert.IsType<PrefabControlComponent>(c),
                c => Assert.IsType<ColorizerComponent>(c));
            Assert.Equal(1, dropped["FuseTestBogusComponentKind"]);
            Assert.Single(dropped);
        }

        // ------------------------------------------------------------------
        // (3) The RE-deserialize paths must NOT re-fire the postfix (that is the
        //     double-apply that broke LegosBetterRollingStock ComponentGroup toggles).
        // ------------------------------------------------------------------
        [Fact]
        public void BypassDeserialize_DoesNotFirePublicDeserializePostfix()
        {
            Assert.NotNull(BypassDeserializeMethod);
            var text = Definitions(BoxcarObject(ArrayAirHose));

            var container = (Container)BypassDeserializeMethod.Invoke(null, new object[] { text });

            Assert.NotNull(container);
            Assert.Equal(0, PostfixCallCount);
            var item = Assert.Single(container.Objects);
            Assert.Equal(BaseIdentifier, item.Identifier);
            Assert.IsType<CarDefinition>(item.Definition);
        }

        [Fact]
        public void MixintoItemRedeserialize_DoesNotFirePublicDeserializePostfix()
        {
            Assert.NotNull(MixintoDeserializeItemMethod);

            var item = (ContainerItem)MixintoDeserializeItemMethod.Invoke(null, new object[] { BoxcarObject(ArrayAirHose) });

            Assert.NotNull(item);
            Assert.Equal(BaseIdentifier, item.Identifier);
            Assert.IsType<CarDefinition>(item.Definition);
            Assert.Equal(0, PostfixCallCount);
        }

        // ------------------------------------------------------------------
        // (3b) Full sequence as it happens in-game for one pack: cold load (1 postfix
        //      pass) followed by a per-item mixinto re-deserialize and a bypass
        //      re-deserialize (0 additional passes). Total stays at exactly one.
        // ------------------------------------------------------------------
        [Fact]
        public void ColdLoadThenRedeserialize_PostfixTotalIsExactlyOne()
        {
            var text = Definitions(BoxcarObject(ArrayAirHose));

            var cold = ColdLoad(text);
            Assert.Equal(1, PostfixCallCount);
            Assert.Contains(cold.Objects, o => o.Identifier == CloneIdentifier);

            MixintoDeserializeItemMethod.Invoke(null, new object[] { BoxcarObject(ArrayAirHose) });
            BypassDeserializeMethod.Invoke(null, new object[] { text });

            Assert.Equal(1, PostfixCallCount);
        }
    }
}
