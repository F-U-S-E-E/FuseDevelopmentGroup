using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    public class StationLocationCompatibilityTests
    {
        [Theory]
        [InlineData("bryson-house")]
        [InlineData("BRYSON-HOUSE")]
        [InlineData("Bryson Depot")]
        [InlineData("hemingway-depot")]
        [InlineData("Hemingway Station")]
        public void MappedStation_ResolvesPassengerStop(string industryId)
        {
            var found = StationAPI.TryGetLocationPassengerStopId(
                industryId,
                out var passengerStopId);

            Assert.True(found);
            Assert.Equal(
                industryId.StartsWith("hemingway", System.StringComparison.OrdinalIgnoreCase)
                    ? "hemingway"
                    : "bryson",
                passengerStopId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("ela-house")]
        [InlineData("bryson-freight")]
        public void UnmappedIndustry_DoesNotBorrowPassengerStop(string industryId)
        {
            var found = StationAPI.TryGetLocationPassengerStopId(
                industryId,
                out var passengerStopId);

            Assert.False(found);
            Assert.Null(passengerStopId);
        }
    }
}
