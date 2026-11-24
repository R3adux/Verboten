using System.Collections.Generic;
using Verboten.Core;
using Xunit;

namespace Verboten.Tests
{
    public class GeometryTests
    {
        private static readonly List<LandingZone> NoZones = new List<LandingZone>();
        private static readonly List<Claim> NoClaims = new List<Claim>();

        [Fact]
        public void LandingZone_IsSpawnRadiusPlus20()
        {
            var zone = new LandingZone { X = 0, Z = 0, SpawnRadius = 30 };
            Assert.Equal(50f, zone.ProtectedRadius);
            // claim edge 46 m away (anchor at 51): too close by 4 m, move away (east)
            var p = ClaimGeometry.Check(Make.Claim(51, 0, 5), NoClaims, new[] { zone });
            Assert.NotNull(p);
            Assert.Contains("4 m too close to the landing zone", p.Reason);
            Assert.Equal("west", ClaimGeometry.Compass(p.MoveX, p.MoveZ)); // +X is west on the in-game compass
            Assert.Null(ClaimGeometry.Check(Make.Claim(55, 0, 5), NoClaims, new[] { zone }));
        }

        [Fact]
        public void Gap_10m_RequiredAndDirectionSuggested()
        {
            var other = Make.Claim(0, 0, 5, owner: Make.Friend);   // -5..5
            var near = Make.Claim(16, 0, 5);                       // 11..21 → gap 6
            var p = ClaimGeometry.Check(near, new[] { other }, NoZones);
            Assert.NotNull(p);
            Assert.Equal("west", ClaimGeometry.Compass(p.MoveX, p.MoveZ));
            Assert.Null(ClaimGeometry.Check(Make.Claim(20, 0, 5), new[] { other }, NoZones));
        }

        [Theory]
        [InlineData(0, -1, "north")]       // in-game compass: north = -Z, east = -X
        [InlineData(-1, -1, "north-east")]
        [InlineData(-1, 0, "east")]
        [InlineData(0, 1, "south")]
        [InlineData(1, 0, "west")]
        [InlineData(1, -1, "north-west")]
        public void Compass(float dx, float dz, string expected) => Assert.Equal(expected, ClaimGeometry.Compass(dx, dz));

        [Fact]
        public void Reach_LimitsAndMaxSpan()
        {
            var c = Make.Claim(0, 0, 5);
            Assert.Null(ClaimGeometry.CheckReach(c, Side.North, 45));
            Assert.NotNull(ClaimGeometry.CheckReach(c, Side.North, 46));
            Assert.NotNull(ClaimGeometry.CheckReach(c, Side.North, 4));
            c.SetReach(Side.South, 10);
            Assert.NotNull(ClaimGeometry.CheckReach(c, Side.North, 41));   // 41 + 10 = 51 > 50
            Assert.Null(ClaimGeometry.CheckReach(c, Side.North, 40));
        }

        [Fact]
        public void SteelForArea_RisesWithTheClaimsArea()
        {
            Assert.Equal(30, ClaimGeometry.SteelForArea(900, 30, 1.0, 0.0));        // 30 m² at 1 per m²
            double bigger = ClaimGeometry.SteelForArea(900, 30, 1.0, 0.1);
            Assert.True(bigger > 30);                                               // bigger claims pay more per m²
            Assert.True(ClaimGeometry.SteelForArea(1800, 30, 1.0, 0.1) > bigger);  // and even more when bigger still
            Assert.Equal(0, ClaimGeometry.SteelForArea(900, 0, 1.0, 0.1));
        }

        [Theory]
        [InlineData(0f, -1f, Side.North)]   // in-game compass: north = -Z, east = -X
        [InlineData(-1f, 0f, Side.East)]
        [InlineData(0f, 1f, Side.South)]
        [InlineData(1f, 0f, Side.West)]
        [InlineData(0.4f, -0.9f, Side.North)] // looking a bit to the side: still north
        public void Facing_PicksTheSideInFront(float x, float z, Side expected) => Assert.Equal(expected, ClaimGeometry.Facing(x, z));

        [Fact]
        public void ShrinkToClear_CutsTheCheapestSide()
        {
            var abandoned = Make.Claim(0, 0, 20);  // -20..20
            abandoned.State = ClaimState.Abandoned;
            // a new active claim 15..25 on the east: needs 10 m gap → east edge at 5
            Assert.True(ClaimGeometry.ShrinkToClear(abandoned, 15, 25, -5, 5, ClaimGeometry.Gap));
            Assert.Equal(5f, abandoned.MaxX);
            Assert.Equal(-20f, abandoned.MinX);
            Assert.True(abandoned.GapTo(15, 25, -5, 5) >= ClaimGeometry.Gap);
        }

        [Fact]
        public void ShrinkToClear_StructurePointInside()
        {
            var c = Make.Claim(0, 0, 20);
            Assert.True(ClaimGeometry.ShrinkToClear(c, 15, 15, 0, 0, 0f));  // a structure at x = 15
            Assert.False(c.Contains(15, 0));
            Assert.True(c.Contains(0, 0));
        }

        [Fact]
        public void ShrinkToClear_FailsWhenTooCloseToAnchor()
        {
            var c = Make.Claim(0, 0, 20);
            // a claim right next to the anchor: every cut would leave less than 5 m
            Assert.False(ClaimGeometry.ShrinkToClear(c, 2, 8, -8, 8, ClaimGeometry.Gap));
        }
    }

    /// <summary>Anchor "Flag": the rectangle around the staked flags.</summary>
    public class StakeTests
    {
        [Fact]
        public void FourCorners_AreTheBorder()
        {
            ClaimGeometry.StakeBounds(new[] { (0f, 0f), (20f, 0f), (20f, 30f), (0f, 30f) }, out var minX, out var maxX, out var minZ, out var maxZ);
            Assert.Equal((0f, 20f, 0f, 30f), (minX, maxX, minZ, maxZ));
        }

        [Fact]
        public void OutermostFlagsCount_InnerOnesDoNot()
        {
            ClaimGeometry.StakeBounds(new[] { (0f, 0f), (40f, 5f), (10f, 25f), (20f, 10f) }, out var minX, out var maxX, out var minZ, out var maxZ);
            Assert.Equal((0f, 40f, 0f, 25f), (minX, maxX, minZ, maxZ));
        }

        [Fact]
        public void FlagsInARow_StillGive10m()
        {
            ClaimGeometry.StakeBounds(new[] { (0f, 0f), (10f, 0f), (20f, 0f), (30f, 0f) }, out var minX, out var maxX, out var minZ, out var maxZ);
            Assert.Equal(30f, maxX - minX, 3);
            Assert.Equal(10f, maxZ - minZ, 3);
            Assert.Equal(-5f, minZ, 3);
        }
    }

    /// <summary>Growing switched off: claims cover their base and follow it (ClaimGeometry.GrowToCover).</summary>
    public class FollowBaseTests
    {
        private static readonly List<LandingZone> NoZones = new List<LandingZone>();
        private static readonly List<Claim> NoClaims = new List<Claim>();

        [Fact]
        public void CoversTheBasePlus5m()
        {
            var c = Make.Claim(0, 0, 5);                                    // -5..5
            Assert.True(ClaimGeometry.GrowToCover(c, -12, 8, -3, 20, NoClaims, NoZones, null));
            Assert.Equal(-17f, c.MinX, 3); Assert.Equal(13f, c.MaxX, 3);
            Assert.Equal(-8f, c.MinZ, 3); Assert.Equal(25f, c.MaxZ, 3);
        }

        [Fact]
        public void NeverShrinks()
        {
            var c = Make.Claim(0, 0, 15);
            Assert.False(ClaimGeometry.GrowToCover(c, -2, 2, -2, 2, NoClaims, NoZones, null));
            Assert.Equal(30f, c.Width, 3);
        }

        [Fact]
        public void StopsAt50x50_GrowingEvenly()
        {
            var c = Make.Claim(0, 0, 5);
            ClaimGeometry.GrowToCover(c, -40, 40, 0, 0, NoClaims, NoZones, null);
            Assert.Equal(50f, c.Width, 3);
            Assert.Equal(-25f, c.MinX, 3); Assert.Equal(25f, c.MaxX, 3);
        }

        [Fact]
        public void NeverFurtherThan45mFromTheAnchor()
        {
            var c = Make.Claim(0, 0, 5);
            ClaimGeometry.GrowToCover(c, 0, 60, 0, 0, NoClaims, NoZones, null);
            Assert.Equal(45f, c.MaxX, 3);
        }

        [Fact]
        public void KeepsTheGapToOtherClaims()
        {
            var other = Make.Claim(35, 0, 5, owner: Make.Friend);           // 30..40
            var c = Make.Claim(0, 0, 5);
            ClaimGeometry.GrowToCover(c, 0, 40, 0, 0, new[] { other }, NoZones, null);
            Assert.Equal(20f, c.MaxX, 3);                                   // 10 m gap
        }

        [Fact]
        public void StaysOutOfTheLandingZone()
        {
            var zone = new LandingZone { X = 40, Z = 0, SpawnRadius = 10 }; // protected up to x = 10
            var c = Make.Claim(0, 0, 5);
            ClaimGeometry.GrowToCover(c, 0, 30, 0, 0, NoClaims, new[] { zone }, null);
            Assert.Equal(10f, c.MaxX, 3);
        }

        [Fact]
        public void StopsBeforeOtherPlayersStructures()
        {
            var c = Make.Claim(0, 0, 5);
            ClaimGeometry.GrowToCover(c, 0, 20, 0, 0, NoClaims, NoZones, new[] { (12f, 0f) });
            Assert.True(c.MaxX < 12f && c.MaxX >= 11f);
            Assert.False(c.Contains(12f, 0f));
            Assert.Equal(-5f, c.MinX, 3);                                   // the other sides were not asked to move
        }

        [Fact]
        public void KeepsOtherPlayersBuildingsOutOfTheZone()
        {
            // a building 30 m away must not end up in the claim's 10 m zone (its builder could no longer build there)
            var c = Make.Claim(0, 0, 5);
            ClaimGeometry.GrowToCover(c, 0, 40, 0, 0, NoClaims, NoZones, new[] { (30f, 0f) });
            Assert.True(c.MaxX < 20f && c.MaxX >= 19f);
            Assert.False(c.Near(30f, 0f, ClaimGeometry.Zone));
        }
    }
}
