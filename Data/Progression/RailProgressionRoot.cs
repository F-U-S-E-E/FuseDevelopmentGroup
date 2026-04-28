using System.Collections.Generic;

namespace RAIL.Data
{
    public sealed class RailProgressionRoot
    {
        public Dictionary<string, RailProgression> Progressions { get; set; } = new Dictionary<string, RailProgression>();
        public Dictionary<string, RailMapFeature> MapFeatures { get; set; } = new Dictionary<string, RailMapFeature>();
    }

    public sealed class RailProgression
    {
        public Dictionary<string, RailSection> Sections { get; set; } = new Dictionary<string, RailSection>();
    }

    public sealed class RailSection
    {
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string[] PrerequisiteSectionIds { get; set; }
        public string[] EnableFeaturesOnUnlock { get; set; }
        public string[] DisableFeaturesOnUnlock { get; set; }
        public string[] EnableFeaturesOnAvailable { get; set; }
        public RailDeliveryPhase[] DeliveryPhases { get; set; }
    }

    public sealed class RailDeliveryPhase
    {
        public int Cost { get; set; }
        public string IndustryComponentId { get; set; }
        public RailDelivery[] Deliveries { get; set; }
    }

    public sealed class RailDelivery
    {
        public string CarTypeFilter { get; set; }
        public string LoadId { get; set; }
        public int Count { get; set; }
        public string DestinationIndustryId { get; set; }
    }

    public sealed class RailMapFeature
    {
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public bool InitiallyEnabled { get; set; }
        public string[] GroupIds { get; set; }
    }
}
