using System.Numerics;
using DiscoAccess.Core.World;
using Xunit;

namespace DiscoAccess.Tests
{
    public class SpatialReadoutTests
    {
        private static readonly Vector3 Player = new Vector3(0f, 0f, 0f);

        [Fact]
        public void Coincident_ReadsHere()
        {
            Assert.Equal("here", SpatialReadout.Describe(Player, new Vector3(0.01f, 0f, 0f)));
        }

        [Fact]
        public void BearingFirst_ThenDistance()
        {
            // 3 metres due east.
            Assert.Equal("east, 3 meters", SpatialReadout.Describe(Player, new Vector3(3f, 0f, 0f)));
        }

        [Fact]
        public void OneMetre_IsSingular()
        {
            Assert.Equal("north, 1 meter", SpatialReadout.Describe(Player, new Vector3(0f, 0f, 1f)));
        }

        [Fact]
        public void SubMetre_ReadsLessThanAMeter()
        {
            // Past the "here" epsilon but rounds to 0 metres.
            Assert.Equal("north, less than a meter", SpatialReadout.Describe(Player, new Vector3(0f, 0f, 0.3f)));
        }

        [Fact]
        public void VerticalOffset_AppendsAboveBelow()
        {
            Assert.Equal("east, 3 meters, above", SpatialReadout.Describe(Player, new Vector3(3f, 2f, 0f)));
            Assert.Equal("east, 3 meters, below", SpatialReadout.Describe(Player, new Vector3(3f, -2f, 0f)));
        }
    }
}
