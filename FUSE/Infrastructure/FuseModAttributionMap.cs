using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FUSE.Loading;

namespace FUSE.Infrastructure
{
    /// <summary>
    /// Maps observed exception evidence (a Unity stack-trace string, or a
    /// listener's recipient type) to the third-party mod that owns it, for
    /// the legacy-mod health monitor.
    ///
    /// Unity stack traces carry namespace-qualified type names, never
    /// assembly names, so stack attribution works on a token map: each known
    /// mod assembly contributes its simple name plus the first one and two
    /// dot-segments of every namespace it declares. Tokens that collide with
    /// game/engine/FUSE-owned namespaces — or with another mod — attribute
    /// to nobody (dropped into the denylist), so a wrong blame is impossible
    /// at the cost of an occasional unattributed count.
    ///
    /// The map is built lazily on first use (mods are all loaded well before
    /// the first exception can be observed) and invalidated whenever the mod
    /// population changes. Building reflects over UMM's mod entries the same
    /// way <see cref="FuseUmmState"/> does (per-member reflection, tolerant
    /// of shape drift) and over FUSE's own hosted legacy plugins. All
    /// runtime harvesting is fail-open: a build failure yields an empty map
    /// (everything unattributed) and one warning, never a throw into the
    /// recording path.
    ///
    /// The parsing core (<see cref="TryAttributeStackCore"/>) and the token
    /// map assembly (<see cref="BuildTokenMapCore"/>) are pure so tests run
    /// them without Unity or UMM present.
    /// </summary>
    internal static class FuseModAttributionMap
    {
        // Innermost frames are examined first — the throwing mod gets blamed,
        // not a FUSE/game frame that hosted the call. Deep traces past this
        // cap are almost certainly engine plumbing; stop rather than scan.
        private const int MaxFramesExamined = 12;

        private static readonly object Gate = new object();

        // Both maps are immutable once published; readers take the reference
        // without the lock. _built is volatile so the map writes are visible
        // before the flag flips.
        private static Dictionary<string, (string modId, string displayName)> _tokenMap;
        private static Dictionary<Assembly, (string modId, string displayName)> _assemblyMap;
        private static volatile bool _built;
        private static bool _loggedBuildFailure;
        private static bool _loggedDroppedTokens;

        // Roots that must never attribute to a mod even if a mod declares
        // them: game/engine/framework namespaces plus FUSE itself. The build
        // additionally harvests every namespace root the game and FUSE
        // assemblies actually declare, so this seed list only has to cover
        // assemblies too large to scan (System.*, UnityEngine.*).
        private static readonly string[] DenylistSeedTokens =
        {
            "System", "Microsoft", "Mono", "mscorlib", "netstandard",
            "Unity", "UnityEngine", "UnityEngineInternal", "UnityModManagerNet",
            "TMPro", "Newtonsoft", "Serilog", "JetBrains",
            "FUSE", "HarmonyLib", "Harmony", "GalaSoft"
        };

        /// <summary>
        /// Attribute a Unity stack-trace string to a known mod. Innermost
        /// matching frame wins; returns false (outs null) when no frame in
        /// the first 12 belongs to a mapped mod.
        /// </summary>
        internal static bool TryAttributeStack(
            string stackTrace, out string modId, out string displayName, out string topOwnedFrame)
        {
            EnsureBuilt();
            return TryAttributeStackCore(stackTrace, _tokenMap, out modId, out displayName, out topOwnedFrame);
        }

        /// <summary>
        /// Attribute a recipient type to a known mod by its assembly — exact,
        /// no string parsing. Used for exceptions FUSE contained itself.
        /// </summary>
        internal static bool TryAttributeType(Type type, out string modId, out string displayName)
        {
            modId = null;
            displayName = null;
            if (type == null)
            {
                return false;
            }

            EnsureBuilt();
            var map = _assemblyMap;
            if (map == null || map.Count == 0)
            {
                return false;
            }

            Assembly assembly;
            try
            {
                assembly = type.Assembly;
            }
            catch
            {
                return false;
            }

            if (assembly != null && map.TryGetValue(assembly, out var owner))
            {
                modId = owner.modId;
                displayName = owner.displayName;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Drop the built maps; the next attribution rebuilds. Call when the
        /// mod population changes (UMM injection flush, legacy assembly load).
        /// </summary>
        internal static void Invalidate()
        {
            lock (Gate)
            {
                _built = false;
            }
        }

        /// <summary>
        /// Pure parsing core. Unity trace lines look like
        /// "Namespace.Type.Method (args) (at file:line)"; Mono exception
        /// traces prefix "at " and append IL offsets; Debug.Log context
        /// traces use "Namespace.Type:Method()". Each line is cut before its
        /// argument list, its first one/two dot-segments are looked up in
        /// <paramref name="tokenMap"/> (two-segment first, so "Us.Dchn" style
        /// roots win over a generic first word), and the first — innermost —
        /// matching frame is returned.
        /// </summary>
        internal static bool TryAttributeStackCore(
            string stackTrace,
            IReadOnlyDictionary<string, (string modId, string displayName)> tokenMap,
            out string modId,
            out string displayName,
            out string topOwnedFrame)
        {
            modId = null;
            displayName = null;
            topOwnedFrame = null;
            if (string.IsNullOrEmpty(stackTrace) || tokenMap == null || tokenMap.Count == 0)
            {
                return false;
            }

            var examined = 0;
            var position = 0;
            while (position < stackTrace.Length && examined < MaxFramesExamined)
            {
                var newline = stackTrace.IndexOf('\n', position);
                var lineEnd = newline < 0 ? stackTrace.Length : newline;
                var line = stackTrace.Substring(position, lineEnd - position).Trim();
                position = lineEnd + 1;
                if (line.Length == 0)
                {
                    continue;
                }

                examined++;
                if (line.StartsWith("at ", StringComparison.Ordinal))
                {
                    line = line.Substring(3).TrimStart();
                }

                var argumentsStart = line.IndexOf(" (", StringComparison.Ordinal);
                var framePath = argumentsStart >= 0 ? line.Substring(0, argumentsStart) : line;
                framePath = framePath.TrimEnd();
                if (framePath.Length == 0)
                {
                    continue;
                }

                // Tokens come from the type path only; the Debug.Log-style
                // ":Method()" tail stays in the reported frame but not in
                // the lookup key.
                var tokenSource = framePath;
                var colon = tokenSource.IndexOf(':');
                if (colon >= 0)
                {
                    tokenSource = tokenSource.Substring(0, colon).TrimEnd();
                }

                var firstDot = tokenSource.IndexOf('.');
                if (firstDot <= 0)
                {
                    continue; // no namespace-qualified identifier on this line
                }

                var secondDot = tokenSource.IndexOf('.', firstDot + 1);
                var twoSegments = secondDot > 0 ? tokenSource.Substring(0, secondDot) : tokenSource;
                if (tokenMap.TryGetValue(twoSegments, out var byTwoSegments))
                {
                    modId = byTwoSegments.modId;
                    displayName = byTwoSegments.displayName;
                    topOwnedFrame = framePath;
                    return true;
                }

                var oneSegment = tokenSource.Substring(0, firstDot);
                if (tokenMap.TryGetValue(oneSegment, out var byOneSegment))
                {
                    modId = byOneSegment.modId;
                    displayName = byOneSegment.displayName;
                    topOwnedFrame = framePath;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Pure token-map assembly: applies the denylist and drops tokens
        /// claimed by more than one mod (an ambiguous token attributes to
        /// nobody — once two mods claim it, it joins the denylist so a third
        /// claim is also refused). Returns an OrdinalIgnoreCase map.
        /// </summary>
        internal static Dictionary<string, (string modId, string displayName)> BuildTokenMapCore(
            IEnumerable<(string token, string modId, string displayName)> candidates,
            IEnumerable<string> denylistTokens,
            out int droppedTokens)
        {
            droppedTokens = 0;
            var denied = new HashSet<string>(denylistTokens ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var map = new Dictionary<string, (string modId, string displayName)>(StringComparer.OrdinalIgnoreCase);
            if (candidates == null)
            {
                return map;
            }

            foreach (var candidate in candidates)
            {
                var token = candidate.token?.Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (denied.Contains(token))
                {
                    droppedTokens++;
                    continue;
                }

                if (map.TryGetValue(token, out var existing))
                {
                    if (!string.Equals(existing.modId, candidate.modId, StringComparison.OrdinalIgnoreCase))
                    {
                        map.Remove(token);
                        denied.Add(token);
                        droppedTokens++;
                    }

                    continue;
                }

                map.Add(token, (candidate.modId, candidate.displayName));
            }

            return map;
        }

        /// <summary>
        /// Test seam: publish maps directly so registry/attribution wiring is
        /// testable without a live UMM/legacy-host population.
        /// </summary>
        internal static void SetMapsForTests(
            Dictionary<string, (string modId, string displayName)> tokenMap,
            Dictionary<Assembly, (string modId, string displayName)> assemblyMap)
        {
            lock (Gate)
            {
                _tokenMap = tokenMap ??
                    new Dictionary<string, (string modId, string displayName)>(StringComparer.OrdinalIgnoreCase);
                _assemblyMap = assemblyMap ?? new Dictionary<Assembly, (string modId, string displayName)>();
                _built = true;
            }
        }

        /// <summary>Test hook: forget maps and one-shot log latches.</summary>
        internal static void ResetForTests()
        {
            lock (Gate)
            {
                _tokenMap = null;
                _assemblyMap = null;
                _built = false;
                _loggedBuildFailure = false;
                _loggedDroppedTokens = false;
            }
        }

        private static void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            lock (Gate)
            {
                if (_built)
                {
                    return;
                }

                try
                {
                    BuildLocked();
                }
                catch (Exception ex)
                {
                    _tokenMap = new Dictionary<string, (string modId, string displayName)>(StringComparer.OrdinalIgnoreCase);
                    _assemblyMap = new Dictionary<Assembly, (string modId, string displayName)>();
                    if (!_loggedBuildFailure)
                    {
                        _loggedBuildFailure = true;
                        FuseLog.Warning(
                            $"FUSE could not build the mod attribution map; exceptions will count as unattributed: {ex.GetBaseException().Message}");
                    }
                }

                _built = true;
            }
        }

        private static void BuildLocked()
        {
            var denylist = new HashSet<string>(DenylistSeedTokens, StringComparer.OrdinalIgnoreCase);

            Assembly[] domainAssemblies;
            try
            {
                domainAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            }
            catch
            {
                domainAssemblies = Array.Empty<Assembly>();
            }

            // Harvest every root the game/FUSE actually declare so mod tokens
            // that shadow game namespaces (e.g. "Game", "Track") never win.
            var ownAssembly = typeof(FuseModAttributionMap).Assembly;
            foreach (var assembly in domainAssemblies)
            {
                if (!IsOwnedForDenylist(assembly, ownAssembly))
                {
                    continue;
                }

                foreach (var token in HarvestTokens(assembly))
                {
                    denylist.Add(token);
                }
            }

            var candidates = new List<(Assembly assembly, string modId, string displayName)>();
            HarvestUmmMods(candidates, domainAssemblies);
            HarvestHostedLegacyPlugins(candidates);

            var assemblyMap = new Dictionary<Assembly, (string modId, string displayName)>();
            foreach (var candidate in candidates)
            {
                if (candidate.assembly == null || candidate.assembly == ownAssembly)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(candidate.modId))
                {
                    continue;
                }

                if (!assemblyMap.ContainsKey(candidate.assembly))
                {
                    assemblyMap.Add(candidate.assembly, (candidate.modId, candidate.displayName));
                }
            }

            var tokenCandidates = new List<(string token, string modId, string displayName)>();
            foreach (var pair in assemblyMap)
            {
                foreach (var token in HarvestTokens(pair.Key))
                {
                    tokenCandidates.Add((token, pair.Value.modId, pair.Value.displayName));
                }
            }

            _tokenMap = BuildTokenMapCore(tokenCandidates, denylist, out var droppedTokens);
            _assemblyMap = assemblyMap;

            if (droppedTokens > 0 && !_loggedDroppedTokens)
            {
                _loggedDroppedTokens = true;
                FuseLog.Info(
                    $"FUSE mod attribution map dropped {droppedTokens} colliding namespace token(s); affected frames will count as unattributed.");
            }
        }

        // Kept in its own method so a missing UMM assembly surfaces as a JIT
        // failure at this call site, inside EnsureBuilt's catch — not as a
        // type-initialization fault on the whole class.
        private static void HarvestUmmMods(
            List<(Assembly assembly, string modId, string displayName)> candidates,
            Assembly[] domainAssemblies)
        {
            try
            {
                var field = typeof(UnityModManagerNet.UnityModManager)
                    .GetField("modEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var entries = field?.GetValue(null) as IEnumerable;
                if (entries == null)
                {
                    return;
                }

                // Location lookup for multi-DLL mods: any loaded assembly
                // sitting under a mod's folder belongs to that mod.
                var locations = new List<(Assembly assembly, string location)>();
                foreach (var assembly in domainAssemblies)
                {
                    var location = SafeAssemblyLocation(assembly);
                    if (!string.IsNullOrEmpty(location))
                    {
                        locations.Add((assembly, NormalizePath(location)));
                    }
                }

                foreach (var entry in entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    var info = ReadObjectMember(entry, "Info");
                    var id = ReadStringMember(entry, "Id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        id = ReadStringMember(info, "Id");
                    }

                    var displayName = ReadStringMember(entry, "DisplayName");
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = ReadStringMember(info, "DisplayName");
                    }

                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = id;
                    }

                    if (string.IsNullOrWhiteSpace(id) ||
                        string.Equals(id, "FUSE", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var entryAssembly = ReadObjectMember(entry, "Assembly") as Assembly;
                    if (entryAssembly != null)
                    {
                        candidates.Add((entryAssembly, id, displayName));
                    }

                    var path = ReadStringMember(entry, "Path");
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    var folder = NormalizePath(path) + Path.DirectorySeparatorChar;
                    foreach (var located in locations)
                    {
                        if (located.location.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                        {
                            candidates.Add((located.assembly, id, displayName));
                        }
                    }
                }
            }
            catch
            {
                // UMM absent or reshaped: the legacy-host source still works.
                FuseModExceptionRegistry.CountSelfFault();
            }
        }

        private static void HarvestHostedLegacyPlugins(
            List<(Assembly assembly, string modId, string displayName)> candidates)
        {
            try
            {
                foreach (var hosted in FuseLegacyAssemblyHost.EnumerateAllHostedPlugins())
                {
                    var assembly = hosted.PluginType?.Assembly;
                    var id = hosted.Manifest?.Id;
                    if (assembly == null || string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var displayName = hosted.Manifest.Name;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = id;
                    }

                    candidates.Add((assembly, id, displayName));
                }
            }
            catch
            {
                // Host unavailable: UMM harvesting alone still attributes.
                FuseModExceptionRegistry.CountSelfFault();
            }
        }

        private static bool IsOwnedForDenylist(Assembly assembly, Assembly ownAssembly)
        {
            if (assembly == null)
            {
                return false;
            }

            if (assembly == ownAssembly)
            {
                return true;
            }

            var name = SafeAssemblyName(assembly);
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // Only the scannable owned assemblies: the seed list covers the
            // huge framework/engine surfaces without a type scan.
            return name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "0Harmony", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("GalaSoft", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> HarvestTokens(Assembly assembly)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var simpleName = SafeAssemblyName(assembly);
            if (!string.IsNullOrEmpty(simpleName) && seen.Add(simpleName))
            {
                yield return simpleName;
            }

            foreach (var type in SafeGetTypes(assembly))
            {
                var ns = type?.Namespace;
                if (string.IsNullOrEmpty(ns))
                {
                    continue;
                }

                var firstDot = ns.IndexOf('.');
                var root = firstDot < 0 ? ns : ns.Substring(0, firstDot);
                if (seen.Add(root))
                {
                    yield return root;
                }

                if (firstDot > 0)
                {
                    var secondDot = ns.IndexOf('.', firstDot + 1);
                    var twoSegments = secondDot < 0 ? ns : ns.Substring(0, secondDot);
                    if (seen.Add(twoSegments))
                    {
                        yield return twoSegments;
                    }
                }
            }
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null).ToArray();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static string SafeAssemblyName(Assembly assembly)
        {
            try
            {
                return assembly?.GetName().Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeAssemblyLocation(Assembly assembly)
        {
            try
            {
                return assembly?.Location ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static object ReadObjectMember(object instance, string name)
        {
            if (instance == null)
            {
                return null;
            }

            var type = instance.GetType();
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(instance, null);
        }

        private static string ReadStringMember(object instance, string name)
        {
            var value = ReadObjectMember(instance, name);
            return value == null ? string.Empty : value.ToString();
        }
    }
}
