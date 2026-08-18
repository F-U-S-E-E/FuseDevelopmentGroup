using System;
using System.Runtime.Serialization;
using FUSE.Runtime.API;
using Game.Progression;
using Xunit;

namespace FUSE.Tests.API
{
    public class ProgressionSectionInitializationTests
    {
        [Fact]
        public void EnsureSectionCollectionsInitialized_DefaultsNullCollections()
        {
            var section = (Section)FormatterServices.GetUninitializedObject(typeof(Section));

            ProgressionAPI.EnsureSectionCollectionsInitialized(section);

            Assert.Empty(section.deliveryPhases);
            Assert.Empty(section.prerequisiteSections);
            Assert.Empty(section.enableFeaturesOnUnlock);
            Assert.Empty(section.enableFeaturesOnAvailable);
            Assert.Empty(section.disableFeaturesOnUnlock);
        }

        [Fact]
        public void EnsureSectionCollectionsInitialized_PreservesExistingCollections()
        {
            var section = (Section)FormatterServices.GetUninitializedObject(typeof(Section));
            var phases = new[] { new Section.DeliveryPhase() };
            var prerequisites = new Section[1];
            var enableOnUnlock = new MapFeature[1];
            var enableOnAvailable = new MapFeature[1];
            var disableOnUnlock = new MapFeature[1];
            section.deliveryPhases = phases;
            section.prerequisiteSections = prerequisites;
            section.enableFeaturesOnUnlock = enableOnUnlock;
            section.enableFeaturesOnAvailable = enableOnAvailable;
            section.disableFeaturesOnUnlock = disableOnUnlock;

            ProgressionAPI.EnsureSectionCollectionsInitialized(section);

            Assert.Same(phases, section.deliveryPhases);
            Assert.Same(prerequisites, section.prerequisiteSections);
            Assert.Same(enableOnUnlock, section.enableFeaturesOnUnlock);
            Assert.Same(enableOnAvailable, section.enableFeaturesOnAvailable);
            Assert.Same(disableOnUnlock, section.disableFeaturesOnUnlock);
        }
    }
}
