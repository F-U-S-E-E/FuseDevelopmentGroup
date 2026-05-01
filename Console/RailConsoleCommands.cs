using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RAIL.Cache;
using RAIL.Infrastructure;
using RAIL.Loading;
using RAIL.Patches;
using RAIL.Registry;
using RAIL.Validation;
using Track;
using UI.Console;

namespace RAIL.Console
{
    internal static class RailConsoleCommands
    {
        public static IList<IConsoleCommand> CreateAll()
        {
            return new List<IConsoleCommand>
            {
                new RailReportCommand(),
                new RailLoadedCommand(),
                new RailGroupsCommand(),
                new RailValidateCommand(),
                new RailConflictsCommand(),
                new RailSuppressionsCommand(),
                new RailPatchesCommand(),
                new RailReapplyCommand(),
                new RailRestoreCommand()
            };
        }

        internal static bool IsInSession()
        {
            // Best-effort: a populated graph means a map is loaded and gameplay
            // is in or near runtime. Refuse destructive console actions then.
            try
            {
                return Graph.Shared != null && Graph.Shared.HasPopulatedCollections;
            }
            catch
            {
                return false;
            }
        }

        internal static string SessionGuardMessage(string commandName, string[] components)
        {
            var hasForce = components != null && components.Any(arg =>
                string.Equals(arg, "--force", StringComparison.OrdinalIgnoreCase));
            if (!IsInSession() || hasForce)
            {
                return null;
            }

            return $"{commandName} refused: a map is currently loaded. Pass --force to override " +
                   "(may destabilize the running save).";
        }
    }

    [ConsoleCommand("/rail.report", "Show the last human-readable RAIL map-load report.")]
    public sealed class RailReportCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            return RailLoadReport.GetLastDetailReport();
        }
    }

    [ConsoleCommand("/rail.loaded", "List loaded RAIL packages and their applied/faulted state.")]
    public sealed class RailLoadedCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var sb = new StringBuilder();
            var faulted = RailPackageFaultRegistry.GetFaultedPackageIds();
            var ids = RailModLoader.GetLoadedMods()
                .Concat(faulted)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            sb.AppendLine($"RAIL loaded packages: {ids.Length}");
            foreach (var id in ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var statuses = new List<string>();
                statuses.Add(RailModLoader.IsApplied(id) ? "applied" : "loaded-not-applied");
                if (RailPackageFaultRegistry.IsFaulted(id))
                {
                    statuses.Add("faulted");
                }

                var status = string.Join(", ", statuses.ToArray());
                sb.AppendLine($"  {id}  [{status}]");
            }

            return sb.ToString();
        }
    }

    [ConsoleCommand("/rail.groups", "List runtime track groups discovered on the active graph.")]
    public sealed class RailGroupsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            try
            {
                var graph = Graph.Shared;
                if (graph == null || !graph.HasPopulatedCollections)
                {
                    return "RAIL groups: track graph is not populated yet.";
                }

                var groups = graph.Segments
                    .Where(seg => seg != null && !string.IsNullOrWhiteSpace(seg.groupId))
                    .GroupBy(seg => seg.groupId, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var sb = new StringBuilder();
                sb.AppendLine($"RAIL track groups: {groups.Length} (segments-with-group / total {graph.Segments.Count()}).");
                foreach (var group in groups)
                {
                    sb.AppendLine($"  {group.Key}  segments={group.Count()}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"RAIL groups failed: {ex.Message}";
            }
        }
    }

    [ConsoleCommand("/rail.validate", "Re-run the RAIL validator for a loaded mod id.")]
    public sealed class RailValidateCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var modId = components != null && components.Length > 0 ? components[0] : null;
            if (string.IsNullOrWhiteSpace(modId))
            {
                return "Usage: /rail.validate <modId>";
            }

            var definition = RailModLoader.GetLoadedDefinition(modId);
            if (definition == null)
            {
                return $"RAIL validate: mod '{modId}' is not loaded.";
            }

            var result = new RailDefinitionValidator().Validate(definition);
            var sb = new StringBuilder();
            sb.AppendLine($"RAIL validate '{modId}': errors={result.Errors.Count} warnings={result.Warnings.Count}");
            foreach (var error in result.Errors)
            {
                sb.AppendLine($"  [error] {error.Field}: {error.Message} ({error.Code ?? string.Empty})");
            }

            foreach (var warning in result.Warnings)
            {
                sb.AppendLine($"  [warn ] {warning.Field}: {warning.Message} ({warning.Code ?? string.Empty})");
            }

            return sb.ToString();
        }
    }

    [ConsoleCommand("/rail.conflicts", "List RAIL registry conflicts (recorded ownership collisions).")]
    public sealed class RailConflictsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var conflicts = RailRegistry.Conflicts;
            var sb = new StringBuilder();
            sb.AppendLine(
                $"RAIL registry: exclusive={RailRegistry.ExclusiveClaimCount} shared={RailRegistry.SharedClaimCount} " +
                $"conflicts={conflicts.Count}");
            foreach (var conflict in conflicts.OrderByDescending(c => c.AtUtc))
            {
                sb.AppendLine(
                    $"  {conflict.Kind} '{conflict.Id}': owner='{conflict.OwnerPackageId}' " +
                    $"attempted='{conflict.AttemptedPackageId}' at={conflict.AtUtc:HH:mm:ss}Z");
            }

            return sb.ToString();
        }
    }

    [ConsoleCommand("/rail.suppressions", "List active RAIL world suppressions.")]
    public sealed class RailSuppressionsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var scenePaths = RailWorldSuppressor.GetActiveScenePathSuppressions()
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var groups = RailWorldSuppressor.GetActiveTrackGroupSuppressions()
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var areas = RailWorldSuppressor.GetActiveAreaSuppressions()
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var sb = new StringBuilder();
            sb.AppendLine(
                $"RAIL suppressions: scenePaths={scenePaths.Length} trackGroups={groups.Length} areas={areas.Length}.");
            AppendSuppressionList(sb, "scene paths", scenePaths);
            AppendSuppressionList(sb, "track groups", groups);
            AppendSuppressionList(sb, "areas", areas);
            return sb.ToString();
        }

        private static void AppendSuppressionList(StringBuilder sb, string label, IEnumerable<string> values)
        {
            var items = (values ?? Enumerable.Empty<string>()).ToArray();
            if (items.Length == 0)
            {
                return;
            }

            sb.AppendLine("  " + label + ":");
            foreach (var item in items)
            {
                sb.AppendLine("    " + item);
            }
        }
    }

    [ConsoleCommand("/rail.patches", "List Harmony patch classes applied or skipped by RAIL.")]
    public sealed class RailPatchesCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"RAIL Harmony patches: applied={RailPatchResilience.Applied.Count} failed={RailPatchResilience.Failed.Count}");
            foreach (var info in RailPatchResilience.Applied.OrderBy(p => p.TypeName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  [ok  ] {info.TypeName}");
            }

            foreach (var info in RailPatchResilience.Failed.OrderBy(p => p.TypeName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  [fail] {info.TypeName}: {info.FailureReason}");
            }

            return sb.ToString();
        }
    }

    [Experimental("Mid-session reapply may destabilize a running save; gated by --force.")]
    [ConsoleCommand("/rail.reapply", "[experimental] Re-apply loaded RAIL definitions. Refused while a map is loaded unless --force is passed.")]
    public sealed class RailReapplyCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            RailExperimentalLog.WarnFirstUse(
                "RAIL.Console./rail.reapply",
                "mid-session reapply via console");

            var guard = RailConsoleCommands.SessionGuardMessage("/rail.reapply", components);
            if (guard != null)
            {
                return guard;
            }

            try
            {
                RailCacheRegistry.RebuildAll();
                var applied = RailDataPackageDiscovery.ApplyLoadedPackages("rail.reapply console");
                return $"RAIL reapply: applied={applied} resident definition(s).";
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL reapply console command failed.", ex);
                return $"RAIL reapply failed: {ex.Message}";
            }
        }
    }

    [Experimental("Full unload + disk reload + reapply; not safe mid-session, gated by --force.")]
    [ConsoleCommand("/rail.restore", "[experimental] Reload RAIL packages from disk and reapply. Refused while a map is loaded unless --force is passed.")]
    public sealed class RailRestoreCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            RailExperimentalLog.WarnFirstUse(
                "RAIL.Console./rail.restore",
                "mid-session full restore via console");

            var guard = RailConsoleCommands.SessionGuardMessage("/rail.restore", components);
            if (guard != null)
            {
                return guard;
            }

            try
            {
                RailModLoader.UnloadAll();
                RailCacheRegistry.ClearAll();
                var loaded = RailDataPackageDiscovery.LoadPackagesFromDisk(true);
                RailCacheRegistry.RebuildAll();
                var applied = RailDataPackageDiscovery.ApplyLoadedPackages("rail.restore console");
                return $"RAIL restore: loadedFromDisk={loaded} appliedToRuntime={applied}.";
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL restore console command failed.", ex);
                return $"RAIL restore failed: {ex.Message}";
            }
        }
    }
}
