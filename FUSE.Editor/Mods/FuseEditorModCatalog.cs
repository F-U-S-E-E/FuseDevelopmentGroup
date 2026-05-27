using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FUSE.Infrastructure;
using FUSE.Loading;
using Newtonsoft.Json.Linq;

namespace FUSE.Editor.Mods
{
    /// <summary>
    /// Filesystem-driven discovery of mods under a Railroader
    /// <c>Mods/</c> folder. Classifies each subfolder so the mod
    /// browser can show FUSE mods, legacy mods (with a convert
    /// affordance), and code-only mods correctly. Pure logic with
    /// <see cref="FuseLog"/> for diagnostics; no Unity dependencies
    /// so the catalog is fully xUnit-testable via temp folders.
    /// </summary>
    /// <remarks>
    /// We deliberately classify off filesystem markers rather than
    /// reading actual JSON payloads where possible — file IO is the
    /// expensive bit and the markers are stable.
    ///
    /// Marker rules:
    /// <list type="bullet">
    ///   <item><c>*.fuse.json</c> in folder → <c>FuseMod</c>.</item>
    ///   <item><c>Definition.json</c> with <c>requires.id == "railloader"</c>
    ///     → <c>LegacyRailLoader</c>.</item>
    ///   <item><c>mapmod.yaml</c> or <c>mapmod.json</c> in folder →
    ///     <c>LegacyMapMod</c>.</item>
    ///   <item><c>Info.json</c> with an <c>AssemblyName</c> field but
    ///     no FUSE / legacy markers → <c>CodeOnlyMod</c>.</item>
    ///   <item>Anything else → <c>Unknown</c>.</item>
    /// </list>
    /// </remarks>
    internal static class FuseEditorModCatalog
    {
        public static IReadOnlyList<FuseEditorModEntry> EnumerateAll(string modsRootPath)
        {
            var results = new List<FuseEditorModEntry>();
            if (string.IsNullOrEmpty(modsRootPath) || !Directory.Exists(modsRootPath))
            {
                return results;
            }

            string[] subFolders;
            try
            {
                subFolders = Directory.GetDirectories(modsRootPath);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor mod catalog: failed to enumerate '{modsRootPath}'.", ex);
                return results;
            }

            foreach (var folder in subFolders)
            {
                try
                {
                    var entry = Classify(folder);
                    if (entry != null)
                    {
                        results.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE editor mod catalog: failed to classify '{folder}'.", ex);
                }
            }

            results.Sort((a, b) => string.Compare(a.DisplayName ?? a.Id, b.DisplayName ?? b.Id, StringComparison.OrdinalIgnoreCase));
            return results;
        }

        /// <summary>
        /// Classifies a single mod folder. Public for test access; the
        /// browser calls <see cref="EnumerateAll"/> in production.
        /// </summary>
        public static FuseEditorModEntry Classify(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return null;
            }

            var entry = new FuseEditorModEntry
            {
                Id = Path.GetFileName(folderPath),
                DisplayName = Path.GetFileName(folderPath),
                FolderPath = folderPath,
                Kind = FuseEditorModKind.Unknown,
            };

            // FuseMod: any *.fuse.json file (definition payload).
            var fuseFiles = Directory.GetFiles(folderPath, "*.fuse.json", SearchOption.TopDirectoryOnly);
            if (fuseFiles.Length > 0)
            {
                entry.Kind = FuseEditorModKind.FuseMod;
                TryReadFuseManifest(folderPath, entry);
                return entry;
            }

            // LegacyMapMod: an AMM patch file. Either name works; AMM
            // historically shipped mapmod.yaml but the JSON variant
            // shows up in some converted exports.
            if (File.Exists(Path.Combine(folderPath, "mapmod.yaml")) ||
                File.Exists(Path.Combine(folderPath, "mapmod.json")))
            {
                entry.Kind = FuseEditorModKind.LegacyMapMod;
                entry.IneligibilityReason = "Legacy AMM map mod — convert to FUSE before editing.";
                TryReadInfoJson(folderPath, entry);
                return entry;
            }

            // LegacyRailLoader: Definition.json with railloader in requires.
            var defJsonPath = Path.Combine(folderPath, "Definition.json");
            if (File.Exists(defJsonPath) && IsRailLoaderDefinition(defJsonPath, entry))
            {
                entry.Kind = FuseEditorModKind.LegacyRailLoader;
                entry.IneligibilityReason = "Legacy Railloader mod — convert to FUSE before editing.";
                return entry;
            }

            // CodeOnlyMod: UMM Info.json but no data markers. Likely a
            // pure-code mod we can't edit content for.
            var infoJsonPath = Path.Combine(folderPath, "Info.json");
            if (File.Exists(infoJsonPath))
            {
                entry.Kind = FuseEditorModKind.CodeOnlyMod;
                entry.IneligibilityReason = "Code-only mod — no editable content.";
                TryReadInfoJson(folderPath, entry);
                return entry;
            }

            // Unknown: no markers we recognise.
            entry.IneligibilityReason = "Folder doesn't look like a Railroader mod.";
            return entry;
        }

        private static bool IsRailLoaderDefinition(string definitionPath, FuseEditorModEntry entry)
        {
            try
            {
                var text = File.ReadAllText(definitionPath);
                var json = JObject.Parse(text);

                entry.Id = json.Value<string>("id") ?? entry.Id;
                entry.DisplayName = json.Value<string>("name") ?? entry.DisplayName;
                entry.Version = json.Value<string>("version");

                var requires = json["requires"] as JArray;
                if (requires == null)
                {
                    return false;
                }

                foreach (var token in requires)
                {
                    var requireId = token.Value<string>("id");
                    if (string.Equals(requireId, "railloader", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor mod catalog: Definition.json parse failed for '{definitionPath}'.", ex);
                return false;
            }
        }

        private static void TryReadInfoJson(string folderPath, FuseEditorModEntry entry)
        {
            var path = Path.Combine(folderPath, "Info.json");
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                entry.Id = json.Value<string>("Id") ?? entry.Id;
                entry.DisplayName = json.Value<string>("DisplayName") ?? entry.DisplayName;
                entry.Author = json.Value<string>("Author");
                entry.Version = json.Value<string>("Version");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor mod catalog: Info.json parse failed for '{path}'.", ex);
            }
        }

        /// <summary>
        /// Reads the first *.fuse.json found to extract a friendly
        /// display name + author + version for the browser row.
        /// Info.json's values are preferred when present (UMM manifest
        /// is canonical for mod identity); .fuse.json fields fill the
        /// gaps. The folder-name defaults from <see cref="Classify"/>
        /// only survive when neither file supplied a real value.
        /// </summary>
        private static void TryReadFuseManifest(string folderPath, FuseEditorModEntry entry)
        {
            var fuseFiles = Directory.GetFiles(folderPath, "*.fuse.json", SearchOption.TopDirectoryOnly);
            if (fuseFiles.Length == 0)
            {
                return;
            }

            // Read .fuse.json FIRST (it's the FUSE-side manifest), then
            // let Info.json override since it's the authoritative UMM
            // manifest. Either path overrides the folder-name defaults
            // we set in Classify.
            try
            {
                var json = JObject.Parse(File.ReadAllText(fuseFiles[0]));
                ApplyIfPresent(entry, "Id", json.Value<string>("Id"));
                ApplyIfPresent(entry, "Name", json.Value<string>("Name"));
                ApplyIfPresent(entry, "Author", json.Value<string>("Author"));
                ApplyIfPresent(entry, "Version", json.Value<string>("ModVersion"));
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor mod catalog: .fuse.json parse failed for '{fuseFiles[0]}'.", ex);
            }

            TryReadInfoJson(folderPath, entry);
        }

        private static void ApplyIfPresent(FuseEditorModEntry entry, string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            switch (field)
            {
                case "Id": entry.Id = value; break;
                case "Name": entry.DisplayName = value; break;
                case "Author": entry.Author = value; break;
                case "Version": entry.Version = value; break;
            }
        }

        /// <summary>
        /// Creates a new FUSE mod folder under <paramref name="modsRootPath"/>
        /// with a minimal Info.json + matching .fuse.json. The folder
        /// is named after the sanitized <paramref name="modId"/>.
        /// Returns the absolute path on success, or null with a logged
        /// reason on failure.
        /// </summary>
        public static string CreateNewMod(string modsRootPath, string modId, string displayName, string author)
        {
            if (string.IsNullOrWhiteSpace(modsRootPath))
            {
                FuseLog.Warning("FUSE editor mod catalog: CreateNewMod skipped because mods root path was empty.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(modId))
            {
                FuseLog.Warning("FUSE editor mod catalog: CreateNewMod skipped because mod id was empty.");
                return null;
            }

            var sanitized = SanitizeId(modId);
            if (string.IsNullOrEmpty(sanitized))
            {
                FuseLog.Warning($"FUSE editor mod catalog: CreateNewMod skipped because mod id '{modId}' had no valid characters.");
                return null;
            }

            var folder = Path.Combine(modsRootPath, sanitized);
            if (Directory.Exists(folder))
            {
                FuseLog.Warning($"FUSE editor mod catalog: CreateNewMod skipped because folder '{folder}' already exists.");
                return null;
            }

            try
            {
                Directory.CreateDirectory(folder);

                // Minimal Info.json mirroring the UMM manifest shape;
                // the EntryMethod is FUSE.FusePlugin.Load since the
                // mod inherits FUSE as a hard requirement.
                var info = new JObject
                {
                    ["Id"] = sanitized,
                    ["DisplayName"] = string.IsNullOrWhiteSpace(displayName) ? sanitized : displayName.Trim(),
                    ["Author"] = string.IsNullOrWhiteSpace(author) ? "(unknown)" : author.Trim(),
                    ["Version"] = "0.1.0",
                };
                File.WriteAllText(Path.Combine(folder, "Info.json"), info.ToString(Newtonsoft.Json.Formatting.Indented));

                // Empty FUSE definition. The schema layer fills in
                // defaults for missing sections at load time so we
                // don't have to pre-populate every collection.
                var fuse = new JObject
                {
                    ["SchemaVersion"] = "1.0",
                    ["Id"] = sanitized,
                    ["Name"] = string.IsNullOrWhiteSpace(displayName) ? sanitized : displayName.Trim(),
                    ["Author"] = string.IsNullOrWhiteSpace(author) ? "(unknown)" : author.Trim(),
                    ["ModVersion"] = "0.1.0",
                };
                File.WriteAllText(Path.Combine(folder, sanitized + ".fuse.json"), fuse.ToString(Newtonsoft.Json.Formatting.Indented));

                FuseLog.Info($"FUSE editor mod catalog: created new mod scaffold at '{folder}'.");
                return folder;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor mod catalog: CreateNewMod failed for '{folder}'.", ex);
                return null;
            }
        }

        /// <summary>
        /// Stable id for the auto-scaffolded "Untitled" scratch mod
        /// the editor lands the user in when no other mod is active.
        /// Picking a deterministic id (not a timestamp) means we
        /// reuse the same folder across editor sessions instead of
        /// accumulating orphan untitled-NN folders.
        /// </summary>
        public const string ScratchModId = "local.untitled-fuse-editor-scratch";

        /// <summary>
        /// Returns the existing scratch mod if it's already loaded,
        /// otherwise scaffolds it under <paramref name="modsRootPath"/>
        /// and tries to return the freshly-loaded handle from
        /// <see cref="FuseModLoader.GetLoadedModsInOrder"/>. Returns
        /// <c>null</c> if scaffolding fails or the loader hasn't
        /// picked up the new mod yet (e.g. when called before the
        /// loader has run for the current session).
        /// </summary>
        /// <remarks>
        /// Idempotent: a second call returns the same mod handle
        /// without recreating the folder. Folder existence on disk
        /// short-circuits the <see cref="CreateNewMod"/> call so we
        /// never warn about "folder already exists".
        /// </remarks>
        public static FuseLoadedMod EnsureScratchMod(string modsRootPath)
        {
            // Fast path: scratch mod is already loaded.
            var loaded = FuseModLoader.GetLoadedModsInOrder();
            if (loaded != null)
            {
                for (int i = 0; i < loaded.Count; i++)
                {
                    if (string.Equals(loaded[i]?.Definition?.Id, ScratchModId, StringComparison.OrdinalIgnoreCase))
                    {
                        return loaded[i];
                    }
                }
            }

            // Slow path: scaffold on disk if absent. The folder name
            // is the same as the id since SanitizeId is identity for
            // this all-lowercase / dot-separated id.
            if (string.IsNullOrEmpty(modsRootPath))
            {
                FuseLog.Warning("FUSE editor mod catalog: EnsureScratchMod skipped because mods root path was empty.");
                return null;
            }

            var folder = Path.Combine(modsRootPath, ScratchModId);
            if (!Directory.Exists(folder))
            {
                var created = CreateNewMod(modsRootPath, ScratchModId, "Untitled Mod", "(scratch)");
                if (created == null)
                {
                    // CreateNewMod already logged the failure reason.
                    return null;
                }
            }

            // Re-query the loader. If the loader hasn't run since the
            // scaffold (typical at first editor entry when the
            // scratch folder didn't exist at boot), the new mod
            // won't be in the loaded list yet — caller activates a
            // null and falls back to the mod browser as a safety net.
            loaded = FuseModLoader.GetLoadedModsInOrder();
            if (loaded != null)
            {
                for (int i = 0; i < loaded.Count; i++)
                {
                    if (string.Equals(loaded[i]?.Definition?.Id, ScratchModId, StringComparison.OrdinalIgnoreCase))
                    {
                        return loaded[i];
                    }
                }
            }

            FuseLog.Info(
                $"FUSE editor mod catalog: scratch mod scaffold at '{folder}' isn't loaded yet; " +
                "the editor will pick it up on the next Railroader launch.");
            return null;
        }

        /// <summary>
        /// Normalises a user-typed mod id into something safe to use
        /// as a folder name. Keeps ASCII letters / digits / dots /
        /// dashes / underscores; folds anything else to <c>-</c>;
        /// collapses repeated separators; trims edges.
        /// </summary>
        public static string SanitizeId(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var chars = raw.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
                {
                    continue;
                }
                chars[i] = '-';
            }

            // Collapse runs of separators to a single dash.
            var collapsed = new System.Text.StringBuilder(chars.Length);
            char? last = null;
            foreach (var c in chars)
            {
                if (c == '-' && last == '-')
                {
                    continue;
                }
                collapsed.Append(c);
                last = c;
            }

            return collapsed.ToString().Trim('-');
        }
    }
}
