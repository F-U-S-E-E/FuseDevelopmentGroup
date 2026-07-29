using UnityModManagerNet;

namespace FUSE.Profiler.Infrastructure
{
    /// <summary>
    /// UMM-persisted settings (XML in the mod folder via ModSettings).
    /// Public mutable fields by XmlSerializer requirement.
    /// </summary>
    public sealed class ProfilerSettings : UnityModManager.ModSettings
    {
        public int UpdatesPerSecond = 2;
        public float CleanupDelaySeconds = 30f;
        public string ToggleKeyName = "F11";

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
