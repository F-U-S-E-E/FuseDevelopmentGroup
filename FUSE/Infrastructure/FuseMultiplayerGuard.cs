using System;
using System.Reflection;

namespace FUSE.Infrastructure
{
    internal static class FuseMultiplayerGuard
    {
        private static readonly Type MultiplayerType = Type.GetType("Network.Multiplayer, Assembly-CSharp");
        private static readonly PropertyInfo IsClientActiveProperty =
            MultiplayerType?.GetProperty("IsClientActive", BindingFlags.Static | BindingFlags.Public);
        private static readonly PropertyInfo IsHostProperty =
            MultiplayerType?.GetProperty("IsHost", BindingFlags.Static | BindingFlags.Public);
        private static readonly PropertyInfo ModeProperty =
            MultiplayerType?.GetProperty("Mode", BindingFlags.Static | BindingFlags.Public);

        private static bool _loggedReflectionFailure;
        private static bool _loggedCompatibilityWarning;
        private static bool _loggedStrictBlockWarning;

        public static bool CanApplyWorldMutations(string operation)
        {
            if (!IsNonHostMultiplayerClient())
            {
                return true;
            }

            if (FuseSettings.BlockNonHostMultiplayerClientWorldApply)
            {
                WarnStrictBlock(operation);
                return false;
            }

            WarnCompatibilityMode(operation);
            return true;
        }

        public static string GetWorldMutationBlockReason(string operation)
        {
            return
                $"FUSE strict multiplayer safety is enabled. Non-host multiplayer client detected during '{NormalizeOperation(operation)}'; " +
                "runtime world mutations are blocked on this client.";
        }

        public static string GetMultiplayerCompatibilityNotice(string operation)
        {
            return
                $"FUSE multiplayer compatibility mode is active during '{NormalizeOperation(operation)}'. " +
                "This client will apply the same local package stack as the host. All players must use the same FUSE version, enabled mods, and package order.";
        }

        public static FuseMultiplayerStatus GetStatus()
        {
            try
            {
                var mode = ReadModeName();
                var clientActive = MultiplayerType != null && IsClientActiveProperty != null && ReadBooleanProperty(IsClientActiveProperty);
                var isHost = MultiplayerType == null || IsHostProperty == null || ReadBooleanProperty(IsHostProperty);
                var role = mode;
                if (string.IsNullOrWhiteSpace(role) || string.Equals(role, "Singleplayer", StringComparison.OrdinalIgnoreCase))
                {
                    role = "Singleplayer";
                }
                else if (isHost)
                {
                    role = "Host";
                }
                else if (clientActive)
                {
                    role = "Client";
                }

                return new FuseMultiplayerStatus
                {
                    ReflectionAvailable = MultiplayerType != null,
                    Mode = string.IsNullOrWhiteSpace(mode) ? "Unknown" : mode,
                    Role = role,
                    IsClientActive = clientActive,
                    IsHost = isHost,
                    MutationPolicy = FuseSettings.BlockNonHostMultiplayerClientWorldApply
                        ? "Strict non-host block"
                        : "Compatibility mode",
                    LocalPackageFingerprint = FuseModSetService.GetActiveSetFingerprint(),
                    LocalPackageSummary = FuseModSetService.GetActiveSetPackageSummary()
                };
            }
            catch (Exception ex)
            {
                if (!_loggedReflectionFailure)
                {
                    _loggedReflectionFailure = true;
                    FuseLog.Warning($"FUSE multiplayer guard could not build status snapshot: {ex.GetBaseException().Message}");
                }

                return new FuseMultiplayerStatus
                {
                    ReflectionAvailable = false,
                    Mode = "Unknown",
                    Role = "Unknown",
                    MutationPolicy = FuseSettings.BlockNonHostMultiplayerClientWorldApply
                        ? "Strict non-host block"
                        : "Compatibility mode",
                    LocalPackageFingerprint = FuseModSetService.GetActiveSetFingerprint(),
                    LocalPackageSummary = FuseModSetService.GetActiveSetPackageSummary()
                };
            }
        }

        private static bool IsNonHostMultiplayerClient()
        {
            try
            {
                if (MultiplayerType == null || IsClientActiveProperty == null || IsHostProperty == null)
                {
                    return false;
                }

                var isClientActive = ReadBooleanProperty(IsClientActiveProperty);
                if (!isClientActive)
                {
                    return false;
                }

                return !ReadBooleanProperty(IsHostProperty);
            }
            catch (Exception ex)
            {
                if (!_loggedReflectionFailure)
                {
                    _loggedReflectionFailure = true;
                    FuseLog.Warning($"FUSE multiplayer guard could not inspect Network.Multiplayer state: {ex.GetBaseException().Message}");
                }

                return false;
            }
        }

        private static bool ReadBooleanProperty(PropertyInfo property)
        {
            return property != null && property.PropertyType == typeof(bool) && (bool)property.GetValue(null, null);
        }

        private static string ReadModeName()
        {
            if (ModeProperty == null)
            {
                return string.Empty;
            }

            var value = ModeProperty.GetValue(null, null);
            return value == null ? string.Empty : value.ToString();
        }

        private static void WarnCompatibilityMode(string operation)
        {
            if (_loggedCompatibilityWarning)
            {
                FuseLog.Info(
                    $"FUSE multiplayer compatibility mode allowed runtime world mutation operation='{NormalizeOperation(operation)}' " +
                    "reason='non-host client applies identical local package stack'.");
                return;
            }

            _loggedCompatibilityWarning = true;
            FuseLog.Warning(GetMultiplayerCompatibilityNotice(operation));
        }

        private static void WarnStrictBlock(string operation)
        {
            if (_loggedStrictBlockWarning)
            {
                FuseLog.Info(
                    $"FUSE skipped runtime world mutation operation='{NormalizeOperation(operation)}' " +
                    "reason='non-host multiplayer client blocked by strict safety setting'.");
                return;
            }

            _loggedStrictBlockWarning = true;
            FuseLog.Warning(GetWorldMutationBlockReason(operation));
        }

        private static string NormalizeOperation(string operation)
        {
            return string.IsNullOrWhiteSpace(operation) ? "unspecified" : operation.Trim();
        }
    }

    internal sealed class FuseMultiplayerStatus
    {
        public bool ReflectionAvailable { get; set; }
        public string Mode { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsClientActive { get; set; }
        public bool IsHost { get; set; }
        public string MutationPolicy { get; set; } = string.Empty;
        public string LocalPackageFingerprint { get; set; } = string.Empty;
        public string LocalPackageSummary { get; set; } = string.Empty;
    }
}
