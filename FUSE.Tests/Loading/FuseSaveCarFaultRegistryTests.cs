using System;
using System.Linq;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Behavioral tests for <see cref="FuseSaveCarFaultRegistry"/>.
    /// The registry is static, so each test must Reset() before and
    /// after — the IDisposable pattern keeps that bookkeeping in one
    /// place even on failure.
    /// </summary>
    public class FuseSaveCarFaultRegistryTests : IDisposable
    {
        public FuseSaveCarFaultRegistryTests()
        {
            FuseSaveCarFaultRegistry.Reset();
        }

        public void Dispose()
        {
            FuseSaveCarFaultRegistry.Reset();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void Empty_registry_reports_zero_count_and_returns_no_faults()
        {
            Assert.Equal(0, FuseSaveCarFaultRegistry.Count);
            Assert.Empty(FuseSaveCarFaultRegistry.GetAll());
        }

        [Fact]
        public void Record_adds_a_fault_and_returns_true()
        {
            var added = FuseSaveCarFaultRegistry.Record(
                "Cyy2",
                "NYO&W",
                "22701",
                "spinecar1",
                "S_WYX_Run_10",
                205.6f,
                true,
                "filtered as orphan");

            Assert.True(added);
            Assert.Equal(1, FuseSaveCarFaultRegistry.Count);

            var only = Assert.Single(FuseSaveCarFaultRegistry.GetAll());
            Assert.Equal("Cyy2", only.CarId);
            Assert.Equal("NYO&W", only.ReportingMark);
            Assert.Equal("22701", only.RoadNumber);
            Assert.Equal("spinecar1", only.MissingPrototypeId);
            Assert.Equal("S_WYX_Run_10", only.LocationSegmentId);
            Assert.Equal(205.6f, only.LocationDistance, 3);
            Assert.True(only.LocationEndIsA);
            Assert.Equal("filtered as orphan", only.Reason);
        }

        [Fact]
        public void Record_dedupes_on_carId_and_missingPrototype()
        {
            var firstAdd = FuseSaveCarFaultRegistry.Record(
                "Cyy2", "NYO&W", "22701", "spinecar1", "S_WYX_Run_10", 0f, false, "first");
            var secondAdd = FuseSaveCarFaultRegistry.Record(
                "Cyy2", "NYO&W", "22701", "spinecar1", "S_WYX_Run_10", 0f, false, "second");

            Assert.True(firstAdd);
            Assert.False(secondAdd);
            Assert.Equal(1, FuseSaveCarFaultRegistry.Count);
            // The first call's reason wins — the dedup short-circuits
            // before any field mutation could happen, so the existing
            // record is unchanged.
            Assert.Equal("first", FuseSaveCarFaultRegistry.GetAll()[0].Reason);
        }

        [Fact]
        public void Record_allows_same_car_with_different_missingPrototype()
        {
            // A pathological save could in principle have the same
            // car id fail twice for different reasons during a
            // multi-stage load. Each (carId, prototype) tuple is a
            // distinct fault, so both records should land.
            FuseSaveCarFaultRegistry.Record("Cyy2", "NYO&W", "22701", "spinecar1", "x", 0f, false, "a");
            FuseSaveCarFaultRegistry.Record("Cyy2", "NYO&W", "22701", "spinecar2", "x", 0f, false, "b");

            Assert.Equal(2, FuseSaveCarFaultRegistry.Count);
        }

        [Fact]
        public void Record_with_no_identifiers_is_dropped()
        {
            // A record with neither carId nor missingPrototypeId has
            // nothing to dedupe against and nothing useful to surface,
            // so the registry refuses it.
            var added = FuseSaveCarFaultRegistry.Record(
                null, null, null, null, null, 0f, false, "junk");

            Assert.False(added);
            Assert.Equal(0, FuseSaveCarFaultRegistry.Count);
        }

        [Fact]
        public void Reset_clears_all_recorded_faults()
        {
            FuseSaveCarFaultRegistry.Record("Cyy2", "NYO&W", "22701", "spinecar1", "x", 0f, false, "r");
            FuseSaveCarFaultRegistry.Record("Cemz", "PRR", "19070", "spinecar2", "x", 0f, false, "r");
            Assert.Equal(2, FuseSaveCarFaultRegistry.Count);

            FuseSaveCarFaultRegistry.Reset();
            Assert.Equal(0, FuseSaveCarFaultRegistry.Count);
        }

        [Fact]
        public void GetAll_returns_records_sorted_by_prototype_then_displayname()
        {
            // Insert in deliberately scrambled order to verify the
            // returned snapshot is sorted, not insertion-order.
            FuseSaveCarFaultRegistry.Record("Cb", "PRR", "19070", "spinecar2", "x", 0f, false, "r");
            FuseSaveCarFaultRegistry.Record("Ca", "NYO&W", "22701", "spinecar1", "x", 0f, false, "r");
            FuseSaveCarFaultRegistry.Record("Cc", "ACL", "1000", "spinecar1", "x", 0f, false, "r");

            var ordered = FuseSaveCarFaultRegistry.GetAll();
            Assert.Equal(3, ordered.Count);
            // Group spinecar1 first, then spinecar2 — and within
            // spinecar1 ACL 1000 sorts ahead of NYO&W 22701.
            Assert.Equal("spinecar1", ordered[0].MissingPrototypeId);
            Assert.Equal("ACL 1000", ordered[0].DisplayName);
            Assert.Equal("spinecar1", ordered[1].MissingPrototypeId);
            Assert.Equal("NYO&W 22701", ordered[1].DisplayName);
            Assert.Equal("spinecar2", ordered[2].MissingPrototypeId);
        }

        [Fact]
        public void DisplayName_falls_back_to_CarId_when_mark_and_number_missing()
        {
            FuseSaveCarFaultRegistry.Record("CarX", null, null, "spinecar1", "x", 0f, false, "r");
            var fault = Assert.Single(FuseSaveCarFaultRegistry.GetAll());
            Assert.Equal("CarX", fault.DisplayName);
        }
    }
}
