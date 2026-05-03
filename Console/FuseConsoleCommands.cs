using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FUSE.Cache;
using FUSE.Infrastructure;
using FUSE.Loading;
using FUSE.Patches;
using FUSE.Registry;
using FUSE.Validation;
using Track;
using UI.Console;

namespace FUSE.Console
{
    internal static class FuseConsoleCommands
    {
        public static IList<IConsoleCommand> CreateAll()
        {
            return new List<IConsoleCommand>
            {
                new FuseReportCommand(),
                new FuseLoadedCommand(),
                new FuseGroupsCommand(),
                new FuseValidateCommand(),
                new FuseConflictsCommand(),
                new FuseSuppressionsCommand(),
                new FusePatchesCommand(),
                new FuseReapplyCommand(),
                new FuseRestoreCommand()
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

    [ConsoleCommand("/fuse.report", "Show the last human-readable FUSE map-load report.")]
    public sealed class FuseReportCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            return FuseLoadReport.GetLastDetailReport();
        }
    }

    [ConsoleCommand("/fuse.loaded", "List loaded FUSE packages and their applied/faulted state.")]
    public sealed class FuseLoadedCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var sb = new StringBuilder();
            var faulted = FusePackageFaultRegistry.GetFaultedPackageIds();
            var ids = FuseModLoader.GetLoadedMods()
                .Concat(faulted)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            sb.AppendLine($"FUSE loaded packages: {ids.Length}");
            foreach (var id in ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var statuses = new List<string>();
                statuses.Add(FuseModLoader.IsApplied(id) ? "applied" : "loaded-not-applied");
                if (FusePackageFaultRegistry.IsFaulted(id))
                {
                    statuses.Add("faulted");
                }

                var status = string.Join(", ", statuses.ToArray());
                sb.AppendLine($"  {id}  [{status}]");
            }

            return sb.ToString();
        }
    }

    [ConsoleCommand("/fuse.groups", "List runtime track groups discovered on the active graph.")]
    public sealed class FuseGroupsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            try
            {
                var graph = Graph.Shared;
                if (graph == null || !graph.HasPopulatedCollections)
                {
                    return "FUSE groups: track graph is not populated yet.";
                }

                var groups = graph.Segments
                    .Where(seg => seg != null && !string.IsNullOrWhiteSpace(seg.groupId))
                    .GroupBy(seg => seg.groupId, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var sb = new StringBuilder();
                sb.AppendLine($"FUSE track groups: {groups.Length} (segments-with-group / total {graph.Segments.Count()}).");
                foreach (var group in groups)
                {
                    sb.AppendLine($"  {group.Key}  segments={group.Count()}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"FUSE groups failed: {ex.Message}";
            }
        }
    }

    [ConsoleCommand("/fuse.validate", "Re-run the FUSE validator for a loaded mod id.")]
    public sealed class FuseValidateCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var modId = components != null && components.Length > 0 ? components[0] : null;
            if (string.IsNullOrWhiteSpace(modId))
            {
                return "Usage: /fuse.validate <modId>";
            }

            var definition = FuseModLoader.GetLoadedDefinition(modId);
            if (definition == null)
            {
                return $"FUSE validate: mod '{modId}' is not loaded.";
            }

            var result = new FuseDefinitionValidator().Validate(definition);
            var sb = new StringBuilder();
            sb.AppendLine($"FUSE validate '{modId}': errors={result.Errors.Count} warnings={result.Warnings.Count}");
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

    [ConsoleCommand("/fuse.conflicts", "List FUSE registry conflicts (recorded ownership collisions).")]
    public sealed class FuseConflictsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var conflicts = FuseRegistry.Conflicts;
            var sb = new StringBuilder();
            sb.AppendLine(
                $"FUSE registry: exclusive={FuseRegistry.ExclusiveClaimCount} shared={FuseRegistry.SharedClaimCount} " +
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

    [ConsoleCommand("/fuse.suppressions", "List active FUSE world suppressions.")]
    public sealed class FuseSuppressionsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var scenePaths = FuseWorldSuppressor.GetActiveScenePathSuppressions()
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var groups = FuseWorldSuppressor.GetActiveTrackGroupSuppressions()
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var areas = FuseWorldSuppressor.GetActiveAreaSuppressions()
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var sb = new StringBuilder();
            sb.AppendLine(
                $"FUSE suppressions: scenePaths={scenePaths.Length} trackGroups={groups.Length} areas={areas.Length}.");
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

    [ConsoleCommand("/fuse.patches", "List Harmony patch classes applied or skipped by FUSE.")]
    public sealed class FusePatchesCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"FUSE Harmony patches: applied={FusePatchResilience.Applied.Count} failed={FusePatchResilience.Failed.Count}");
            foreach (var info in FusePatchResilience.Applied.OrderBy(p => p.TypeName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  [ok  ] {info.TypeName}");
            }

            foreach (var info in FusePatchResilience.Failed.OrderBy(p => p.TypeName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  [fail] {info.TypeName}: {info.FailureReason}");
            }

            return sb.ToString();
        }
    }

    [Experimental("Mid-session reapply may destabilize a running save; gated by --force.")]
    [ConsoleCommand("/fuse.reapply", "[experimental] Re-apply loaded FUSE definitions. Refused while a map is loaded unless --force is passed.")]
    public sealed class FuseReapplyCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            FuseExperimentalLog.WarnFirstUse(
                "FUSE.Console./fuse.reapply",
                "mid-session reapply via console");

            var guard = FuseConsoleCommands.SessionGuardMessage("/fuse.reapply", components);
            if (guard != null)
            {
                return guard;
            }

            try
            {
                FuseCacheRegistry.RebuildAll();
                var applied = FuseDataPackageDiscovery.ApplyLoadedPackages("fuse.reapply console");
                return $"FUSE reapply: applied={applied} resident definition(s).";
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE reapply console command failed.", ex);
                return $"FUSE reapply failed: {ex.Message}";
            }
        }
    }

    [Experimental("Full unload + disk reload + reapply; not safe mid-session, gated by --force.")]
    [ConsoleCommand("/fuse.restore", "[experimental] Reload FUSE packages from disk and reapply. Refused while a map is loaded unless --force is passed.")]
    public sealed class FuseRestoreCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            FuseExperimentalLog.WarnFirstUse(
                "FUSE.Console./fuse.restore",
                "mid-session full restore via console");

            var guard = FuseConsoleCommands.SessionGuardMessage("/fuse.restore", components);
            if (guard != null)
            {
                return guard;
            }

            try
            {
                FuseModLoader.UnloadAll();
                FuseCacheRegistry.ClearAll();
                var loaded = FuseDataPackageDiscovery.LoadPackagesFromDisk(true);
                FuseCacheRegistry.RebuildAll();
                var applied = FuseDataPackageDiscovery.ApplyLoadedPackages("fuse.restore console");
                return $"FUSE restore: loadedFromDisk={loaded} appliedToRuntime={applied}.";
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE restore console command failed.", ex);
                return $"FUSE restore failed: {ex.Message}";
            }
        }
    }
}
