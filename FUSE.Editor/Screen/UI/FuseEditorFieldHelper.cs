using System.Globalization;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Pure-logic helpers for the editor's numeric input fields. Unity
    /// IMGUI gives us raw <see cref="UnityEngine.GUI.TextField"/> — we
    /// have to do parsing + partial-state tolerance ourselves.
    /// </summary>
    /// <remarks>
    /// Mid-typing states like <c>""</c>, <c>"-"</c>, <c>"1."</c>,
    /// <c>"-0."</c> must be allowed so the user can type a real number
    /// without the field reverting after every keystroke. The contract
    /// here returns the current committed value plus a flag indicating
    /// whether the buffer represents a parseable, semantically-different
    /// number ready to commit.
    /// </remarks>
    internal static class FuseEditorFieldHelper
    {
        /// <summary>
        /// Tries to parse a user-edited float string against a baseline
        /// value. Returns whether the buffer represents a complete,
        /// successfully-parsed value that differs from <paramref name="committedValue"/>.
        /// </summary>
        /// <param name="buffer">Raw text from the IMGUI field.</param>
        /// <param name="committedValue">The last committed value the
        /// field is supposed to reflect.</param>
        /// <param name="parsedValue">Receives the parsed value when the
        /// return is <c>true</c>; otherwise the committed value.</param>
        public static bool TryCommitFloat(string buffer, float committedValue, out float parsedValue)
        {
            parsedValue = committedValue;

            if (string.IsNullOrEmpty(buffer))
            {
                return false;
            }

            // Allow partial typing states that aren't yet a complete
            // float. The user can still type after these.
            if (buffer == "-" || buffer == "." || buffer == "-." || buffer == "+.")
            {
                return false;
            }

            // Trailing dot ("1.") parses fine via float.TryParse with
            // InvariantCulture, but we treat it as partial input so the
            // user can keep typing decimals. Trailing minus ("-") was
            // already filtered above; trailing-just-a-zero ("0.") would
            // commit as 0 — acceptable.
            if (buffer.EndsWith("."))
            {
                return false;
            }

            if (!float.TryParse(buffer, NumberStyles.Float, CultureInfo.InvariantCulture, out var candidate))
            {
                return false;
            }

            // Reject NaN / infinity outright — they can never round-trip
            // sensibly into the authoring layer.
            if (float.IsNaN(candidate) || float.IsInfinity(candidate))
            {
                return false;
            }

            // Same-value commit is a no-op so we don't trigger a save
            // for buffer edits that didn't change anything (e.g. user
            // re-typed the same number).
            if (candidate == committedValue)
            {
                return false;
            }

            parsedValue = candidate;
            return true;
        }

        /// <summary>
        /// Formats <paramref name="value"/> in an editor-friendly shape
        /// (invariant culture, no trailing zeros beyond what's needed)
        /// suitable for seeding an IMGUI text buffer.
        /// </summary>
        public static string FormatFloat(float value)
        {
            // Round-trip ("R") format guarantees the parse(format(x)) == x
            // invariant — important so the user's typed value matches
            // what they see displayed across panel rebuilds.
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parses a comma-separated tag list typed by the user into an
        /// array of non-empty trimmed tag strings. Empty input or input
        /// that whittles down to nothing returns an empty array (not
        /// null) so call sites can safely iterate without a null guard.
        /// </summary>
        public static string[] ParseTags(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return System.Array.Empty<string>();
            }

            var parts = raw.Split(',');
            var result = new System.Collections.Generic.List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                var trimmed = parts[i].Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Formats a tag array back into a comma-and-space-separated
        /// string for display in a text field. <c>null</c> or empty
        /// arrays render as an empty string.
        /// </summary>
        public static string FormatTags(string[] tags)
        {
            if (tags == null || tags.Length == 0)
            {
                return string.Empty;
            }

            return string.Join(", ", tags);
        }
    }
}
