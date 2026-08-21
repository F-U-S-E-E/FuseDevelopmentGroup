using System;
using System.Linq;
using FUSE.Infrastructure;
using Model.Ops;
using UI.Builder;
using static FUSE.Interface.InterfaceUtils;

namespace FUSE.Interface.MenuWindow
{
    internal static class IndustryDashboardToolPage
    {
        public static void Build(UIPanelBuilder builder)
        {
            builder.AddTitle("Industry Dashboard", "");
            AddWrappedLabel(
                builder,
                "A live, read-only view of the industries and component storage known to the game's operations controller. " +
                "It remains useful for native FUSE packages and replaces the old ForYourConvenience dashboard when that dependency is requested.",
                58f);

            builder.HStack(row => row.AddButtonCompact("Refresh", builder.Rebuild), 6f).Height(32f);
            builder.Spacer(8f);

            var industries = OpsController.Shared?.AllIndustries?
                .Where(industry => industry != null)
                .OrderBy(industry => industry.ProgressionDisabled)
                .ThenBy(industry => industry.name, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<Industry>();
            builder.AddField("Industries", industries.Length.ToString());
            if (industries.Length == 0)
            {
                builder.AddLabelEmptyState("No operations industries are available in the current scene.");
                return;
            }

            foreach (var industry in industries)
            {
                BuildIndustry(builder, industry);
            }
        }

        private static void BuildIndustry(UIPanelBuilder builder, Industry industry)
        {
            builder.AddSection(string.IsNullOrWhiteSpace(industry.name) ? industry.identifier : industry.name);
            builder.AddField("Id", industry.identifier ?? "(none)");
            builder.AddField("State", industry.ProgressionDisabled ? "Progression disabled" : "Enabled");
            builder.AddField("Contract", industry.usesContract ? "Uses company contract" : "Not contracted");
            builder.AddField("Components", (industry.Components?.Count() ?? 0).ToString());

            try
            {
                foreach (var pair in industry.EnumerateComponentContexts(0f))
                {
                    var component = pair.Item1;
                    var context = pair.Item2;
                    if (component == null)
                    {
                        continue;
                    }

                    var title = string.IsNullOrWhiteSpace(component.DisplayName)
                        ? component.subIdentifier
                        : component.DisplayName;
                    var type = component.GetType().Name;
                    AddWrappedField(builder, title ?? "Component", type + " — " + ComponentState(component), 36f);
                    foreach (var field in component.PanelFields(context))
                    {
                        AddWrappedField(builder, field.Label, field.Text, 34f);
                    }
                }
            }
            catch (Exception ex)
            {
                AddWrappedField(
                    builder,
                    "Component details",
                    "Unavailable: " + ex.GetBaseException().Message,
                    42f);
                FuseLog.Warning(
                    "FUSE Industry Dashboard could not inspect '" + industry.identifier + "': " +
                    ex.GetBaseException().Message);
            }

            builder.Spacer(8f);
        }

        private static string ComponentState(IndustryComponent component)
        {
            var state = component.ProgressionDisabled ? "progression disabled" : "active";
            var spanCount = component.trackSpans?.Length ?? 0;
            return state + ", " + spanCount + " track span(s), cars " +
                   (component.carTypeFilter?.queryString ?? "(none)");
        }
    }
}
