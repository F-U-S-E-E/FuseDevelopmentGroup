namespace Fuse.Core.Model
{
    /// <summary>
    /// Unity-free extraction of the pure setting type/scope normalization
    /// helpers from the shipping <c>FUSE.Infrastructure.FuseModSettingsStore</c>.
    /// The shipping store is Unity-coupled (it persists JSON under
    /// <c>Application.persistentDataPath</c> and logs through <c>FuseLog</c>),
    /// but the validator only needs these three pure members. Extracting them
    /// here lets <see cref="Fuse.Core.Validation.FuseDefinitionValidator"/>
    /// stay game-free while preserving identical normalization behaviour.
    /// </summary>
    public static class FuseSettingTypes
    {
        public const string ScopeUser = "user";
        public const string ScopeProfile = "profile";
        public const string ScopeServer = "server";

        public static string NormalizeType(string type)
        {
            var value = (type ?? string.Empty).Trim().ToLowerInvariant();
            switch (value)
            {
                case "bool":
                case "boolean":
                    return "bool";
                case "enum":
                case "choice":
                case "select":
                    return "enum";
                case "number":
                case "float":
                case "double":
                case "int":
                case "integer":
                    return "number";
                case "path":
                case "file":
                case "folder":
                    return "path";
                case "color":
                case "colour":
                    return "color";
                case "text":
                case "string":
                default:
                    return "text";
            }
        }

        public static string NormalizeScope(string scope)
        {
            var value = (scope ?? string.Empty).Trim().ToLowerInvariant();
            switch (value)
            {
                case "profile":
                case "modset":
                case "mod-set":
                    return ScopeProfile;
                case "server":
                case "shared":
                case "multiplayer":
                    return ScopeServer;
                case "local":
                case "client":
                case "user":
                default:
                    return ScopeUser;
            }
        }
    }
}
