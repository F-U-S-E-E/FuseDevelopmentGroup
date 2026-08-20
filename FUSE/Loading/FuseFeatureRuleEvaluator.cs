using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    public sealed class FuseFeatureEvaluation
    {
        public int RuleCount { get; internal set; }
        public int EnabledRuleCount { get; internal set; }
        public int DisabledRuleCount { get; internal set; }
        public int RemovedObjectCount { get; internal set; }
        public string[] DisabledRuleIds { get; internal set; } = Array.Empty<string>();

        public string Summary => RuleCount == 0
            ? "No feature rules declared."
            : $"{EnabledRuleCount} enabled, {DisabledRuleCount} disabled; {RemovedObjectCount} authored object(s) omitted until reload.";
    }

    internal static class FuseFeatureRuleEvaluator
    {
        internal static FuseFeatureEvaluation Apply(FuseModDefinition definition)
        {
            return Apply(
                definition,
                (key, setting) => FuseModSettingsStore.GetValue(definition, key, setting));
        }

        internal static FuseFeatureEvaluation Apply(
            FuseModDefinition definition,
            Func<string, FuseModSettingDefinition, JToken> valueResolver)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (valueResolver == null)
                throw new ArgumentNullException(nameof(valueResolver));

            var evaluation = new FuseFeatureEvaluation();
            var disabled = new List<string>();
            foreach (var pair in (definition.FeatureRules ?? new Dictionary<string, FuseFeatureRule>())
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var rule = pair.Value;
                evaluation.RuleCount++;
                definition.Settings.TryGetValue(rule.Setting, out var setting);
                var current = valueResolver(rule.Setting, setting);
                if (Matches(current, rule.Operator, rule.Value))
                {
                    evaluation.EnabledRuleCount++;
                    continue;
                }

                evaluation.DisabledRuleCount++;
                disabled.Add(pair.Key);
                evaluation.RemovedObjectCount += RemoveTargets(definition, rule.Targets);
            }

            evaluation.DisabledRuleIds = disabled.ToArray();
            return evaluation;
        }

        internal static bool Matches(JToken current, string rawOperator, JToken expected)
        {
            var operation = string.IsNullOrWhiteSpace(rawOperator)
                ? "equals"
                : rawOperator.Trim();
            if (operation.Equals("equals", StringComparison.OrdinalIgnoreCase))
                return ValuesEqual(current, expected);
            if (operation.Equals("notEquals", StringComparison.OrdinalIgnoreCase))
                return !ValuesEqual(current, expected);

            if (!TryNumber(current, out var actual) || !TryNumber(expected, out var target))
                return false;
            if (operation.Equals("greaterThan", StringComparison.OrdinalIgnoreCase))
                return actual > target;
            if (operation.Equals("greaterThanOrEqual", StringComparison.OrdinalIgnoreCase))
                return actual >= target;
            if (operation.Equals("lessThan", StringComparison.OrdinalIgnoreCase))
                return actual < target;
            if (operation.Equals("lessThanOrEqual", StringComparison.OrdinalIgnoreCase))
                return actual <= target;
            return false;
        }

        private static bool ValuesEqual(JToken left, JToken right)
        {
            if (TryNumber(left, out var leftNumber) && TryNumber(right, out var rightNumber))
                return Math.Abs(leftNumber - rightNumber) <= 0.0000001d;
            return JToken.DeepEquals(left, right);
        }

        private static bool TryNumber(JToken value, out double number)
        {
            number = 0d;
            if (value == null)
                return false;
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
            {
                number = value.Value<double>();
                return !double.IsNaN(number) && !double.IsInfinity(number);
            }
            return double.TryParse(
                value.Type == JTokenType.String ? value.Value<string>() : value.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number) && !double.IsNaN(number) && !double.IsInfinity(number);
        }

        private static int RemoveTargets(FuseModDefinition definition, FuseFeatureTargets targets)
        {
            if (targets == null)
                return 0;
            var removed = 0;
            removed += Remove(definition.Tracks.Nodes, targets.TrackNodes);
            removed += Remove(definition.Tracks.Segments, targets.TrackSegments);
            removed += Remove(definition.Tracks.Spans, targets.TrackSpans);
            removed += Remove(definition.Tracks.Areas, targets.TrackAreas);
            removed += Remove(definition.Operations.Loads, targets.Loads);
            removed += Remove(definition.Operations.Industries, targets.Industries);
            removed += RemoveIndustryComponents(definition, targets.IndustryComponents);
            removed += Remove(definition.Operations.Loaders, targets.Loaders);
            removed += Remove(definition.Operations.Turntables, targets.Turntables);
            removed += Remove(definition.Operations.Stations, targets.Stations);
            removed += Remove(definition.World.Scenery, targets.Scenery);
            removed += Remove(definition.World.Splineys, targets.Splineys);
            removed += Remove(definition.World.WaterSurfaces, targets.WaterSurfaces);
            removed += Remove(definition.World.TelegraphPoles, targets.TelegraphPoles);
            removed += Remove(definition.World.MapLabels, targets.MapLabels);
            removed += Remove(definition.World.MapMasks, targets.MapMasks);
            removed += Remove(definition.World.MapTiles, targets.MapTiles);
            removed += Remove(definition.World.SceneClones, targets.SceneClones);
            removed += Remove(definition.Progression.Progressions, targets.Progressions);
            removed += Remove(definition.Progression.MapFeatures, targets.MapFeatures);
            removed += Remove(definition.Audio.Whistles, targets.Whistles);
            removed += Remove(definition.Audio.Horns, targets.Horns);
            removed += Remove(definition.Audio.Bells, targets.Bells);
            return removed;
        }

        private static int RemoveIndustryComponents(FuseModDefinition definition, IEnumerable<string> ids)
        {
            var removed = 0;
            foreach (var id in ids ?? Array.Empty<string>())
            {
                var separator = id.IndexOf('/');
                if (separator <= 0 || separator == id.Length - 1)
                    continue;
                var industryId = id.Substring(0, separator);
                var componentId = id.Substring(separator + 1);
                if (definition.Operations.Industries.TryGetValue(industryId, out var industry) &&
                    industry?.Components?.Remove(componentId) == true)
                    removed++;
            }
            return removed;
        }

        private static int Remove<T>(IDictionary<string, T> values, IEnumerable<string> ids)
        {
            if (values == null)
                return 0;
            var removed = 0;
            foreach (var id in ids ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(id) && values.Remove(id))
                    removed++;
            return removed;
        }
    }
}
