using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Authoring.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    internal static partial class FuseLegacyDataConverter
    {
        private static readonly object JsonRepairWarningGate = new object();
        private static readonly HashSet<string> ReportedControlCharacterFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static JObject ReadLegacyObject(string path)
        {
            return JObject.Parse(ReadLegacyJsonText(path));
        }

        internal static JObject ReadManifestObject(string path)
        {
            // Manifest files decide whether a package is discovered at all. Keep
            // the harmless RailLoader-era allowances (comments, trailing commas,
            // and stray control bytes), but never invent missing braces here. A
            // structurally incomplete Info.json must remain attributable to the
            // package and surface in /fuse.report with its real line/column.
            var text = File.ReadAllText(path);
            text = StripJsonControlCharacters(text, path);
            text = StripJsonComments(text);
            text = RemoveTrailingCommas(text);
            return JObject.Parse(text);
        }

        // Counterpart for legacy sources whose top-level token is a JSON
        // array (whistles.json / horns.json / bells.json / myhorns.json
        // and so on are top-level arrays in the Strange-Customs era
        // audio-pack convention). Routing those files through
        // ReadLegacyObject would throw "Current JsonReader item is not
        // an object: StartArray" and the loader would silently drop the
        // file, leaving every horn/whistle/bell entry unregistered.
        internal static JArray ReadLegacyArray(string path)
        {
            return JArray.Parse(ReadLegacyJsonText(path));
        }

        private static string ReadLegacyJsonText(string path)
        {
            var text = File.ReadAllText(path);
            text = StripJsonControlCharacters(text, path);
            text = StripJsonComments(text);
            text = RemoveTrailingCommas(text);
            text = CloseUnbalancedJson(text);
            text = RemoveTrailingCommas(text);
            return text;
        }

        // Drop bare ASCII control characters (other than tab/LF/CR) that
        // occasionally slip into hand-edited legacy mod manifests — e.g. a
        // paste from a terminal that included an embedded SYN, or a
        // Ctrl+V Ctrl+V verbatim-insert slip in vim. Newtonsoft rejects
        // those when they show up inside a string, which would otherwise
        // sink the entire legacy mod.
        //
        // This leniency is INTENTIONALLY scoped to the legacy pipeline.
        // Native FUSE definitions (*.fuse.json) load through
        // <see cref="FUSE.Authoring.Serialization.FuseSerializer.FromJson"/>, which
        // hands the text straight to a strict JsonConvert and surfaces
        // Newtonsoft's parser error with line/column. Authors of new
        // FUSE addons are expected to format their JSON correctly; we
        // only smooth over breakage in the legacy ecosystem so that
        // years-old packages keep loading.
        //
        // When we do strip anything, log a loud warning that names the
        // file, the count, and the first offending byte+offset so the
        // author can find and fix it. Silently swallowing the byte
        // would let the authoring bug propagate forever.
        private static string StripJsonControlCharacters(string text, string path)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            System.Text.StringBuilder builder = null;
            var stripped = 0;
            var firstOffset = -1;
            var firstByte = (char)0;
            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                if (current >= 0x20 || current == '\t' || current == '\n' || current == '\r')
                {
                    builder?.Append(current);
                    continue;
                }

                if (builder == null)
                {
                    builder = new System.Text.StringBuilder(text.Length);
                    builder.Append(text, 0, index);
                }

                if (stripped == 0)
                {
                    firstOffset = index;
                    firstByte = current;
                }

                stripped++;
            }

            if (stripped > 0)
            {
                // Convert the offset into a 1-based line:column so the
                // author can find it in their editor without counting
                // bytes.
                var line = 1;
                var column = 1;
                for (var i = 0; i < firstOffset && i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else if (text[i] != '\r')
                    {
                        column++;
                    }
                }

                var warningKey = path ?? string.Empty;
                var report = false;
                lock (JsonRepairWarningGate)
                {
                    report = ReportedControlCharacterFiles.Add(warningKey);
                }

                if (report)
                {
                    FuseLog.Warning(
                        $"FUSE stripped {stripped} stray control byte(s) from legacy file '{path}' " +
                        $"before parsing (first occurrence: 0x{(int)firstByte:X2} at line {line}, column {column}). " +
                        "This is almost always an editor accident (e.g. a Ctrl+V verbatim insert) and the " +
                        "mod author should fix the source file. This warning is shown once per file per session. " +
                        "FUSE only tolerates this on the legacy pipeline; native FUSE addons (*.fuse.json) " +
                        "must be valid JSON.");
                }
            }

            return builder == null ? text : builder.ToString();
        }

        private static string StripJsonComments(string text)
        {
            var output = new System.Text.StringBuilder();
            var inString = false;
            var escaped = false;
            for (var index = 0; index < (text ?? string.Empty).Length; index++)
            {
                var current = text[index];
                if (inString)
                {
                    output.Append(current);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    output.Append(current);
                    continue;
                }

                if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    index += 2;
                    while (index < text.Length && text[index] != '\r' && text[index] != '\n')
                    {
                        index++;
                    }

                    if (index < text.Length)
                    {
                        output.Append(text[index]);
                    }

                    continue;
                }

                if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
                {
                    index += 2;
                    while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/'))
                    {
                        index++;
                    }

                    index = Math.Min(index + 1, text.Length - 1);
                    continue;
                }

                output.Append(current);
            }

            return output.ToString();
        }

        private static string RemoveTrailingCommas(string text)
        {
            string previous;
            var current = text ?? string.Empty;
            do
            {
                previous = current;
                current = Regex.Replace(current, @",\s*([}\]])", "$1");
            }
            while (!string.Equals(previous, current, StringComparison.Ordinal));
            return current;
        }

        private static string CloseUnbalancedJson(string text)
        {
            var stack = new Stack<char>();
            var inString = false;
            var escaped = false;
            foreach (var current in text ?? string.Empty)
            {
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                }
                else if (current == '{')
                {
                    stack.Push('}');
                }
                else if (current == '[')
                {
                    stack.Push(']');
                }
                else if ((current == '}' || current == ']') && stack.Count > 0 && stack.Peek() == current)
                {
                    stack.Pop();
                }
            }

            if (stack.Count == 0)
            {
                return text;
            }

            return (text ?? string.Empty).TrimEnd() + Environment.NewLine + new string(stack.ToArray()) + Environment.NewLine;
        }
    }
}
