namespace FUSE.Authoring.Data
{
    /// <summary>
    /// Pure ID generation for turntable-derived nodes and segments. Lives here,
    /// not on TurntableAPI, so non-runtime callers (validator, ModLoader's
    /// generated-id collectors, tests) can compute IDs without triggering
    /// TurntableAPI's static initializer — which references Track/Graph/Material
    /// types from Assembly-CSharp + UnityEngine and only resolves inside a
    /// running game.
    ///
    /// Two forms per resource:
    ///   - The base format: "{turntableId}.pit.{index:D2}" etc.
    ///   - A legacy form keyed on FuseTurntable.LegacyIdentifier, preserved so
    ///     converted AMM packages keep producing the same node/segment IDs
    ///     they shipped with.
    /// </summary>
    public static class FuseTurntableIds
    {
        public static string GetPitNodeId(string turntableId, int index)
        {
            return $"{turntableId}.pit.{index:D2}";
        }

        public static string GetPitNodeId(string turntableId, int index, FuseTurntable definition)
        {
            var legacyIdentifier = definition?.LegacyIdentifier;
            if (!string.IsNullOrWhiteSpace(legacyIdentifier))
            {
                return $"N{legacyIdentifier}TurntableNode{index}";
            }

            return GetPitNodeId(turntableId, index);
        }

        public static string GetRoundhouseNodeId(string turntableId, int index, FuseTurntable definition)
        {
            var legacyIdentifier = definition?.LegacyIdentifier;
            if (!string.IsNullOrWhiteSpace(legacyIdentifier))
            {
                return $"N{legacyIdentifier}RoundhouseNode{index}";
            }

            return $"{turntableId}.roundhouse.node.{index:D2}";
        }

        public static string GetRoundhouseSegmentId(string turntableId, int index, FuseTurntable definition)
        {
            var legacyIdentifier = definition?.LegacyIdentifier;
            if (!string.IsNullOrWhiteSpace(legacyIdentifier))
            {
                return $"S{legacyIdentifier}RoundhouseSegment{index}";
            }

            return $"{turntableId}.roundhouse.segment.{index:D2}";
        }
    }
}
