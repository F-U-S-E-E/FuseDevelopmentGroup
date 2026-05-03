using System.Collections.Generic;

namespace FUSE.Data
{
    public sealed class FuseProgressionRoot
    {
        public string ProgressionId { get; set; }
        public FuseSection[] Sections { get; set; }
        public Dictionary<string, FuseProgression> Progressions { get; set; } = new Dictionary<string, FuseProgression>();
        public Dictionary<string, FuseMapFeature> MapFeatures { get; set; } = new Dictionary<string, FuseMapFeature>();
    }

    public sealed class FuseProgression
    {
        public Dictionary<string, FuseSection> Sections { get; set; } = new Dictionary<string, FuseSection>();
    }

    public sealed class FuseSection
    {
        public string Id { get; set; }
        public string ProgressionId { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string[] PrerequisiteSections { get; set; }
        public string[] PrerequisiteSectionIds { get; set; }
        public string[] EnableFeaturesOnUnlock { get; set; }
        public string[] DisableFeaturesOnUnlock { get; set; }
        public string[] EnableFeaturesOnAvailable { get; set; }
        public string[] UnlockIncludeIndustries { get; set; }
        public string[] UnlockExcludeIndustries { get; set; }
        public string[] UnlockIncludeIndustryComponents { get; set; }
        public string[] AreasEnableOnUnlock { get; set; }
        public string[] GameObjectsEnableOnUnlock { get; set; }
        public string[] TrackGroupsEnableOnUnlock { get; set; }
        public string[] TrackGroupsAvailableOnUnlock { get; set; }
        public FuseDeliveryPhase[] DeliveryPhases { get; set; }
    }

    public sealed class FuseDeliveryPhase
    {
        public int Cost { get; set; }
        public string IndustryComponentId { get; set; }
        public FuseDelivery[] Deliveries { get; set; }
    }

    public sealed class FuseDelivery
    {
        public string CarTypeFilter { get; set; }
        public string LoadId { get; set; }
        public int Count { get; set; }
        public string Direction { get; set; }
        public string DestinationIndustryId { get; set; }
    }

    public sealed class FuseMapFeature
    {
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public bool InitiallyEnabled { get; set; }

        /// <summary>
        /// Track group ids that become enabled when this feature unlocks. Was
        /// the only field for a long time; kept for back-compat. New code should
        /// prefer <see cref="TrackGroupsEnableOnUnlock"/> + <see cref="TrackGroupsAvailableOnUnlock"/>.
        /// </summary>
        public string[] GroupIds { get; set; }

        /// <summary>Other map features that must already be unlocked before this one.</summary>
        public string[] PrerequisiteFeatureIds { get; set; }

        /// <summary>Track groups to mark "enabled" (live track) on unlock.</summary>
        public string[] TrackGroupsEnableOnUnlock { get; set; }

        /// <summary>Track groups to mark "available" (visible/buildable) on unlock.</summary>
        public string[] TrackGroupsAvailableOnUnlock { get; set; }

        /// <summary>Areas to enable on unlock (resolved by Area.identifier).</summary>
        public string[] AreasEnableOnUnlock { get; set; }

        /// <summary>
        /// GameObjects to enable on unlock. Each entry is a hierarchical scene
        /// path, e.g. "World/Foo/Bar". Resolved at apply time.
        /// </summary>
        public string[] GameObjectsEnableOnUnlock { get; set; }

        /// <summary>Industry ids to include in this feature's unlock graph.</summary>
        public string[] UnlockIncludeIndustries { get; set; }

        /// <summary>Industry ids explicitly excluded from this feature's unlock graph.</summary>
        public string[] UnlockExcludeIndustries { get; set; }

        /// <summary>Industry component ids to include in this feature's unlock graph.</summary>
        public string[] UnlockIncludeIndustryComponents { get; set; }
    }
}
