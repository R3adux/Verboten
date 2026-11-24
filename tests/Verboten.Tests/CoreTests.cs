using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Verboten.Core;
using Xunit;

namespace Verboten.Tests
{
    internal static class Make
    {
        public const ulong Owner = 76561198000000001UL;
        public const ulong Friend = 76561198000000002UL;
        public const ulong Guest = 76561198000000003UL;
        public const ulong Stranger = 76561198000000004UL;

        /// <summary>A square claim reaching <paramref name="reach"/> in every direction from (x, z).</summary>
        public static Claim Claim(float x, float z, float reach = 15f, ulong owner = Owner, string name = null)
        {
            var c = Core.Claim.Create(owner, "Bob", x, 0f, z, 1, reach);
            c.Name = name;
            return c;
        }

        /// <summary>A field claim with its anchor at (x, z) and the given extra fields.</summary>
        public static Claim Fields(float x, float z, params (int I, int J)[] extra)
        {
            var c = Core.Claim.Create(Owner, "Bob", x, 0f, z, 1, 5f, fields: true);
            foreach (var (i, j) in extra) c.AddField(i, j);
            return c;
        }

        public static Claim WithMembers(float x = 0, float z = 0)
        {
            var c = Claim(x, z);
            c.Members.Add(new ClaimMember { SteamId = Friend, Role = Role.Friend, Name = "Fred" });
            c.Members.Add(new ClaimMember { SteamId = Guest, Role = Role.Guest, Name = "Gina" });
            return c;
        }
    }

    public class RoleTests
    {
        [Fact]
        public void Owner_HasEverything() => Assert.Equal(Permission.All, Roles.Of(Role.Owner));

        [Fact]
        public void Friend_HasEverythingButManage() => Assert.Equal(Permission.All & ~Permission.Manage, Roles.Of(Role.Friend));

        [Fact]
        public void Guest_OnlyEntersAndUses()
        {
            var p = Roles.Of(Role.Guest);
            Assert.Equal(Permission.Entry | Permission.Use, p);
        }

        [Theory]
        [InlineData("guest", Role.Guest)]
        [InlineData("GAST", Role.Guest)]
        [InlineData(" friend ", Role.Friend)]
        public void Parse_Known(string text, Role expected)
        {
            Assert.True(Roles.TryParse(text, out var r));
            Assert.Equal(expected, r);
        }

        [Theory]
        [InlineData("owner")]
        [InlineData("")]
        [InlineData(null)]
        public void Parse_RejectsEverythingElse(string text) => Assert.False(Roles.TryParse(text, out _));
    }

    public class ClaimTests
    {
        [Fact]
        public void Create_IsCenteredOnAnchor()
        {
            var c = Make.Claim(100, -50, 5);
            Assert.Equal(10f, c.Width);
            Assert.Equal(10f, c.Depth);
            foreach (Side s in Enum.GetValues(typeof(Side))) Assert.Equal(5f, c.Reach(s));
        }

        [Fact]
        public void SetReach_MovesOneSideOnly()
        {
            var c = Make.Claim(0, 0, 5);
            c.SetReach(Side.North, 25);
            Assert.Equal(25f, c.Reach(Side.North));
            Assert.Equal(5f, c.Reach(Side.South));
            Assert.Equal(30f, c.Depth);
            Assert.Equal(10f, c.Width);
            Assert.Equal(10f, c.EdgeLength(Side.North));
            Assert.Equal(30f, c.EdgeLength(Side.East));
        }

        [Fact]
        public void Contains_EdgesInclusive()
        {
            var c = Make.Claim(0, 0, 15);
            Assert.True(c.Contains(15, -15));
            Assert.False(c.Contains(15.01f, 0));
            Assert.True(c.Near(20, 0, 5f));
            Assert.False(c.Near(20, 0, 4.9f));
        }

        [Fact]
        public void Gap_IsTheLargerAxisGap()
        {
            var a = Make.Claim(0, 0, 5);           // -5..5
            Assert.Equal(10f, a.GapTo(Make.Claim(20, 0, 5)));   // 15..25 → 10 m apart in X
            Assert.Equal(10f, a.GapTo(Make.Claim(20, 20, 5)));  // diagonal: max(10, 10)
            Assert.Equal(0f, a.GapTo(Make.Claim(5, 0, 5)));     // overlapping
            Assert.True(a.Overlaps(Make.Claim(5, 0, 5)));
            Assert.False(a.Overlaps(Make.Claim(10, 0, 5)));     // touching does not overlap
        }

        [Fact]
        public void DistanceTo_Point()
        {
            var c = Make.Claim(0, 0, 5);
            Assert.Equal(0f, c.DistanceTo(1, 1));
            Assert.Equal(5f, c.DistanceTo(10, 0));
            Assert.Equal(5f, c.DistanceTo(8, 9), 3);
        }

        [Fact]
        public void RolesAndPermissions()
        {
            var c = Make.WithMembers();
            Assert.Equal(Role.Owner, c.RoleOf(Make.Owner));
            Assert.Equal(Role.Friend, c.RoleOf(Make.Friend));
            Assert.Equal(Role.Guest, c.RoleOf(Make.Guest));
            Assert.Equal(Role.None, c.RoleOf(Make.Stranger));
            Assert.Equal(Permission.None, c.PermissionsOf(Make.Stranger));
        }

        [Fact]
        public void Abandoned_ProtectsNothing()
        {
            var c = Make.WithMembers();
            c.State = ClaimState.Abandoned;
            Assert.Equal(Permission.All, c.PermissionsOf(Make.Stranger));
        }

        [Fact]
        public void Guardian_OwnerOrFriend_NotGuest()
        {
            var c = Make.WithMembers();
            Assert.False(c.HasGuardianOnline(_ => false));
            Assert.True(c.HasGuardianOnline(id => id == Make.Friend));
            Assert.False(c.HasGuardianOnline(id => id == Make.Guest));
        }

        [Fact]
        public void Incidents_KeepTheLast20()
        {
            var c = Make.Claim(0, 0);
            for (int i = 0; i < 30; i++) c.AddIncident(i, "x" + i);
            Assert.Equal(Claim.MaxIncidents, c.Incidents.Count);
            Assert.Equal("x10", c.Incidents[0].Text);
        }

        [Fact]
        public void DisplayName_FallsBackToOwner()
        {
            Assert.Equal("Bob's base", Make.Claim(0, 0).DisplayName);
            Assert.Equal("Fort", Make.Claim(0, 0, name: "Fort").DisplayName);
        }

        [Theory]
        [InlineData("  My Base  ", "My Base")]
        [InlineData("a<b>c", "abc")]
        [InlineData("   ", null)]
        public void CleanName(string input, string expected) => Assert.Equal(expected, Claim.CleanName(input));

        [Fact]
        public void Clone_IsDeep()
        {
            var c = Make.WithMembers();
            c.AddIncident(1, "a");
            var copy = c.Clone();
            copy.Members.Clear();
            copy.Incidents.Clear();
            copy.SetReach(Side.North, 30);
            Assert.Equal(2, c.Members.Count);
            Assert.Single(c.Incidents);
            Assert.Equal(15f, c.Reach(Side.North));
        }

        [Fact]
        public void Upgrade_ConvertsOldSquareClaims()
        {
            var old = new Claim { LegacyX = 10, LegacyZ = 20, LegacyHalfSize = 15 };
            old.Upgrade();
            Assert.Equal(10f, old.AnchorX);
            Assert.Equal(-5f, old.MinX);
            Assert.Equal(35f, old.MaxZ);
            Assert.Equal(15f, old.Reach(Side.West));
        }
    }

    public class ClaimIndexTests
    {
        [Fact]
        public void Empty_FindsNothing()
        {
            Assert.Null(ClaimIndex.Empty.At(0, 0));
            Assert.Null(ClaimIndex.Empty.FirstBlockingNear(0, 0, 10, Make.Stranger, Permission.Build));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1000, -1000)]
        [InlineData(7.9f, -8.1f)]
        public void At_FindsClaimAcrossCells(float x, float z)
        {
            var c = Make.Claim(x, z, 15);
            var index = new ClaimIndex(new[] { c }, 1);
            Assert.Same(c, index.At(x + 14.9f, z - 14.9f));
            Assert.Null(index.At(x + 15.1f, z));
        }

        [Fact]
        public void At_WorksForStretchedClaims()
        {
            var c = Make.Claim(0, 0, 5);
            c.SetReach(Side.East, 45); // east = -X
            var index = new ClaimIndex(new[] { c }, 1);
            Assert.Same(c, index.At(-44, 0));
            Assert.Null(index.At(6, 0));
        }

        [Fact]
        public void At_MatchesBruteForce()
        {
            var rnd = new Random(42);
            var claims = new List<Claim>();
            for (int i = 0; i < 200; i++)
            {
                var c = Make.Claim((float)(rnd.NextDouble() * 4000 - 2000), (float)(rnd.NextDouble() * 4000 - 2000), 5);
                c.SetReach(Side.North, 5 + (float)rnd.NextDouble() * 40);
                c.SetReach(Side.East, 5 + (float)rnd.NextDouble() * 40);
                claims.Add(c);
            }
            var index = new ClaimIndex(claims, 1);
            for (int i = 0; i < 20000; i++)
            {
                float x = (float)(rnd.NextDouble() * 4200 - 2100), z = (float)(rnd.NextDouble() * 4200 - 2100);
                var hit = index.At(x, z);
                Assert.Equal(claims.Exists(c => c.Contains(x, z)), hit != null);
            }
        }

        [Fact]
        public void Abandoned_AreNotProtected_ButListed()
        {
            var a = Make.Claim(0, 0);
            a.State = ClaimState.Abandoned;
            var index = new ClaimIndex(new[] { a }, 1);
            Assert.Equal(0, index.Count);
            Assert.Null(index.At(0, 0));
            Assert.Same(a, index.AbandonedAt(0, 0));
            Assert.Single(index.Abandoned);
        }

        [Fact]
        public void FirstBlockingNear_RespectsRolesAndZone()
        {
            var c = Make.WithMembers();
            var index = new ClaimIndex(new[] { c }, 1);
            Assert.Same(c, index.FirstBlockingNear(0, 0, 0, Make.Stranger, Permission.Build));
            Assert.Same(c, index.FirstBlockingNear(0, 0, 0, Make.Guest, Permission.Build));
            Assert.Null(index.FirstBlockingNear(0, 0, 0, Make.Guest, Permission.Entry));
            Assert.Null(index.FirstBlockingNear(0, 0, 0, Make.Friend, Permission.Build));
            // 5 m outside the claim: inside the 10 m approach zone
            Assert.Null(index.FirstBlockingNear(20, 0, 0, Make.Stranger, Permission.Build));
            Assert.Same(c, index.FirstBlockingNear(20, 0, ClaimGeometry.Zone, Make.Stranger, Permission.Build));
            Assert.Null(index.FirstBlockingNear(20, 0, ClaimGeometry.Zone, Make.Friend, Permission.Build));
        }

        [Fact]
        public void ZoneRule_NoMansLandBetweenNeighbours()
        {
            // A: -5..5, B: 15..25 (10 m gap). The spot at x = 10 is in both zones.
            var a = Make.Claim(0, 0, 5, owner: Make.Owner);
            var b = Make.Claim(20, 0, 5, owner: Make.Friend);
            var index = new ClaimIndex(new[] { a, b }, 1);
            Assert.NotNull(index.FirstBlockingNear(10, 0, ClaimGeometry.Zone, Make.Owner, Permission.Build));  // B blocks A's owner
            Assert.NotNull(index.FirstBlockingNear(10, 0, ClaimGeometry.Zone, Make.Friend, Permission.Build)); // A blocks B's owner
        }

        [Fact]
        public void TooClose_OwnedBy_ByAnchor()
        {
            var a = Make.Claim(0, 0, 5);
            a.AnchorId = 77;
            var b = Make.Claim(100, 0, 5, owner: Make.Friend);
            var index = new ClaimIndex(new[] { a, b }, 1);
            Assert.Single(index.TooClose(Make.Claim(18, 0, 5, owner: Make.Stranger)));   // 8 m gap
            Assert.Empty(index.TooClose(Make.Claim(20, 0, 5, owner: Make.Stranger)));    // 10 m gap
            Assert.Empty(index.TooClose(Make.Claim(10, 0, 5)));                          // the same owner may touch
            Assert.Single(index.TooClose(Make.Claim(9, 0, 5)));                          // but not overlap
            Assert.Single(index.OwnedBy(Make.Friend));
            Assert.Same(a, index.ByAnchor(77));
            Assert.Null(index.ByAnchor(0));
        }

        [Fact]
        public void Performance_Smoke()
        {
            var rnd = new Random(1);
            var claims = new List<Claim>();
            for (int i = 0; i < 1000; i++)
                claims.Add(Make.Claim((float)(rnd.NextDouble() * 20000 - 10000), (float)(rnd.NextDouble() * 20000 - 10000)));
            var index = new ClaimIndex(claims, 1);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1_000_000; i++) index.At((i % 20000) - 10000, ((i * 7) % 20000) - 10000);
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 1000, $"1M lookups took {sw.ElapsedMilliseconds} ms");
        }
    }

    public class ClaimStoreTests
    {
        [Fact]
        public void Changes_PublishNewSnapshot_OldSnapshotUnchanged()
        {
            var store = new ClaimStore();
            int events = 0;
            store.Changed += () => events++;
            var c = Make.Claim(0, 0);
            store.Add(c);
            var before = store.Index;
            store.Update(c.Id, x => x.Members.Add(new ClaimMember { SteamId = Make.Friend }));
            Assert.Empty(before.Claims[0].Members);
            Assert.Single(store.Index.Claims[0].Members);
            Assert.True(store.Remove(c.Id));
            Assert.Equal(3, events);
        }

        [Fact]
        public void Batch_PublishesOnce()
        {
            var store = new ClaimStore();
            int events = 0;
            store.Changed += () => events++;
            store.Batch(list => { list.Add(Make.Claim(0, 0)); list.Add(Make.Claim(100, 0)); return true; });
            Assert.Equal(1, events);
            Assert.Equal(2, store.Index.Count);
            store.Batch(list => false);
            Assert.Equal(1, events);
        }

        [Fact]
        public void Load_UpgradesAndRepairs()
        {
            var store = new ClaimStore();
            store.Load(new[] { new Claim { Id = null, Members = null, Incidents = null, LegacyHalfSize = 5 }, null });
            Assert.Equal(1, store.Index.Count);
            Assert.Equal(10f, store.Index.Claims[0].Width);
            Assert.NotNull(store.Index.Claims[0].Members);
        }
    }

    public class ClaimFileTests
    {
        [Fact]
        public void Roundtrip()
        {
            var c = Make.WithMembers(12.5f, -7f);
            c.Name = "Fort <Ä&ö>";
            c.SetReach(Side.West, 30);
            var f = Make.Fields(100f, 0f, (1, 0), (1, 1));
            f.Donations.Add(new Donation { SteamId = Make.Friend, Name = "Fred", Slots = 2 });
            f.PaidSlots = 1;
            c.State = ClaimState.Abandoned;
            c.AddIncident(42, "Eve entered");
            using var ms = new MemoryStream();
            ClaimFile.Write(ms, new ClaimFile
            {
                ServerMinutes = 1234,
                Claims = new List<Claim> { c, f },
                Robots = new List<RobotOwner> { new RobotOwner { RobotId = 5, SteamId = Make.Owner } },
            });
            ms.Position = 0;
            var file = ClaimFile.Read(ms);

            Assert.Equal(1234, file.ServerMinutes);
            Assert.Single(file.Robots);
            var r = file.Claims[0];
            Assert.Equal(c.Name, r.Name);
            Assert.Equal(30f, r.Reach(Side.West));
            Assert.Equal(c.MinX, r.MinX);
            var rf = file.Claims[1];
            Assert.Equal(3, rf.Fields.Count);
            Assert.True(rf.HasField(1, 1));
            Assert.Equal(2, rf.Donated);
            Assert.Equal(1, rf.PaidSlots);
            Assert.Equal(f.MaxX, rf.MaxX);
            Assert.Equal(ClaimState.Abandoned, r.State);
            Assert.Equal("Eve entered", r.Incidents[0].Text);
            Assert.Equal(Role.Guest, r.Members[1].Role);
        }

        [Fact]
        public void OldFormat_IsUpgradedOnLoad()
        {
            const string xml = "<?xml version=\"1.0\"?><Verboten Format=\"1\"><Claim Id=\"a\" X=\"10\" Z=\"20\" HalfSize=\"15\" /></Verboten>";
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
            var store = new ClaimStore();
            store.Load(ClaimFile.Read(ms).Claims);
            var c = store.Index.Claims[0];
            Assert.Equal(30f, c.Width);
            Assert.Equal(10f, c.AnchorX);
        }
    }
}

namespace Verboten.Tests
{
    public class CompassSideTests
    {
        [Fact]
        public void Sides_FollowTheInGameCompass()
        {
            // in-game compass: north = -Z, east = -X
            var c = Make.Claim(0, 0, 5);
            c.SetReach(Side.North, 20);
            Assert.Equal(-20f, c.MinZ);
            c.SetReach(Side.East, 30);
            Assert.Equal(-30f, c.MinX);
            c.SetReach(Side.South, 10);
            Assert.Equal(10f, c.MaxZ);
            c.SetReach(Side.West, 15);
            Assert.Equal(15f, c.MaxX);
        }
    }
}
