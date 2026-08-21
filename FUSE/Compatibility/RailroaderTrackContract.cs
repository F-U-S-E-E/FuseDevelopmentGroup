using System;
using System.Reflection;
using Track;

namespace FUSE.Compatibility
{
    /// <summary>
    /// Bridges the released TrackSegment.Style representation and the newer
    /// flags-based graph contract without taking a compile-time dependency on
    /// fields that are absent from either game generation.
    /// </summary>
    internal static class RailroaderTrackContract
    {
        internal const int BridgeFlag = 2;
        internal const int BridgeSupportsSteelFlag = 4;
        internal const int TunnelFlag = 8;
        internal const int YardFlag = 16;

        private static readonly FieldInfo SegmentStyleField =
            typeof(TrackSegment).GetField("style", BindingFlags.Instance | BindingFlags.Public);
        private static readonly FieldInfo SegmentFlagsField =
            typeof(TrackSegment).GetField("flags", BindingFlags.Instance | BindingFlags.Public);
        private static readonly PropertyInfo NodeDiamondProperty =
            typeof(TrackNode).GetProperty("IsDiamond", BindingFlags.Instance | BindingFlags.Public);
        private static readonly FieldInfo NodeDiamondField =
            typeof(TrackNode).GetField("isDiamond", BindingFlags.Instance | BindingFlags.Public);
        private static readonly MethodInfo GraphSetNodeIsDiamondMethod =
            typeof(Graph).GetMethod(
                "SetNodeIsDiamond",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(TrackNode), typeof(bool) },
                null);

        internal static bool GetNodeIsDiamond(TrackNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (NodeDiamondProperty?.CanRead == true)
            {
                return Convert.ToBoolean(NodeDiamondProperty.GetValue(node, null));
            }

            return NodeDiamondField != null && Convert.ToBoolean(NodeDiamondField.GetValue(node));
        }

        internal static void SetNodeIsDiamond(TrackNode node, bool value)
        {
            if (node == null)
            {
                return;
            }

            if (GraphSetNodeIsDiamondMethod != null && Graph.Shared != null)
            {
                GraphSetNodeIsDiamondMethod.Invoke(Graph.Shared, new object[] { node, value });
            }
            else if (NodeDiamondProperty?.CanWrite == true)
            {
                NodeDiamondProperty.SetValue(node, value, null);
            }
            else
            {
                NodeDiamondField?.SetValue(node, value);
            }
        }

        internal static int GetStructureFlags(TrackSegment segment)
        {
            if (segment == null)
            {
                return 0;
            }

            if (SegmentFlagsField != null)
            {
                return Convert.ToInt32(SegmentFlagsField.GetValue(segment));
            }

            return StyleNameToFlags(SegmentStyleField?.GetValue(segment)?.ToString());
        }

        internal static string GetStyleName(TrackSegment segment)
        {
            if (segment == null)
            {
                return "Standard";
            }

            if (SegmentStyleField != null)
            {
                return SegmentStyleField.GetValue(segment)?.ToString() ?? "Standard";
            }

            var flags = GetStructureFlags(segment);
            if ((flags & TunnelFlag) != 0)
            {
                return "Tunnel";
            }

            if ((flags & BridgeFlag) != 0)
            {
                return "Bridge";
            }

            if ((flags & YardFlag) != 0)
            {
                return "Yard";
            }

            return "Standard";
        }

        internal static bool GetBridgeSupportsSteel(TrackSegment segment)
        {
            return (GetStructureFlags(segment) & BridgeSupportsSteelFlag) != 0;
        }

        internal static bool GetYard(TrackSegment segment)
        {
            return (GetStructureFlags(segment) & YardFlag) != 0;
        }

        internal static void ApplyStructure(
            TrackSegment segment,
            string style,
            bool bridgeSupportsSteel,
            bool yard)
        {
            if (segment == null)
            {
                return;
            }

            if (SegmentFlagsField != null)
            {
                var flags = StyleNameToFlags(style);
                if (bridgeSupportsSteel)
                {
                    flags |= BridgeFlag | BridgeSupportsSteelFlag;
                }

                if (yard)
                {
                    flags |= YardFlag;
                }

                SegmentFlagsField.SetValue(segment, Enum.ToObject(SegmentFlagsField.FieldType, flags));
                return;
            }

            if (SegmentStyleField == null)
            {
                return;
            }

            var releasedStyle = string.IsNullOrWhiteSpace(style) ? "Standard" : style.Trim();
            if (yard && string.Equals(releasedStyle, "standard", StringComparison.OrdinalIgnoreCase))
            {
                releasedStyle = "Yard";
            }

            var matchedStyle = Array.Find(
                Enum.GetNames(SegmentStyleField.FieldType),
                name => string.Equals(name, releasedStyle, StringComparison.OrdinalIgnoreCase));
            if (matchedStyle == null)
            {
                matchedStyle = yard ? "Yard" : "Standard";
            }

            SegmentStyleField.SetValue(segment, Enum.Parse(SegmentStyleField.FieldType, matchedStyle));
        }

        internal static int StyleNameToFlags(string style)
        {
            if (string.Equals(style, "bridge", StringComparison.OrdinalIgnoreCase))
            {
                return BridgeFlag;
            }

            if (string.Equals(style, "tunnel", StringComparison.OrdinalIgnoreCase))
            {
                return TunnelFlag;
            }

            if (string.Equals(style, "yard", StringComparison.OrdinalIgnoreCase))
            {
                return YardFlag;
            }

            return 0;
        }
    }
}
