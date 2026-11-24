using System.Collections.Generic;
using System.Linq;
using Verboten.Core;
using Xunit;

namespace Verboten.Tests
{
    /// <summary>Field claims (10 x 10 m fields), claim slots and the outline shown with flares/sparks.</summary>
    public class FieldTests
    {
        private static readonly List<LandingZone> NoZones = new List<LandingZone>();

        [Fact]
        public void NewFieldClaim_Is10x10AroundTheAnchor()
        {
            var c = Make.Fields(100f, 50f);
            Assert.True(c.HasFields);
            Assert.Equal(95f, c.MinX); Assert.Equal(105f, c.MaxX);
            Assert.Equal(45f, c.MinZ); Assert.Equal(55f, c.MaxZ);
            Assert.Equal(100f, c.Area);
            Assert.Equal(1, c.SlotsNeeded);
        }

        [Fact]
        public void LShape_ContainsOnlyItsFields()
        {
            // anchor field (0,0), then two fields to +X and one to +Z from the last: an L
            var c = Make.Fields(0f, 0f, (1, 0), (2, 0), (2, 1));
            Assert.Equal(400f, c.Area);
            Assert.Equal(30f, c.Width); Assert.Equal(20f, c.Depth);
            Assert.True(c.Contains(20f, 10f));     // field (2,1)
            Assert.False(c.Contains(0f, 10f));     // field (0,1): the empty corner of the L
            Assert.True(c.Near(0f, 10f, 5f));      // but right next to it
            Assert.Equal(0f, c.DistanceTo(25f, 15f));
        }

        [Theory]
        [InlineData(5, 1)]    // 50 m
        [InlineData(6, 2)]    // 60 m: a second slot
        [InlineData(10, 2)]   // 100 m
        [InlineData(11, 3)]   // 110 m
        public void Slots_OnePerStarted50m(int fieldsInARow, int slots)
        {
            var c = Make.Fields(0f, 0f);
            for (int i = 1; i < fieldsInARow; i++) c.AddField(i, 0);
            Assert.Equal(slots, c.SlotsNeeded);
        }

        [Fact]
        public void Slots_RectangleAround_100x100_NeedsFour()
        {
            var c = Make.Fields(0f, 0f);
            for (int i = 0; i < 10; i++) { c.AddField(i, 0); c.AddField(0, i); } // a big L
            Assert.Equal(4, c.SlotsNeeded);
        }

        [Fact]
        public void Grow_TakesTheNextFieldInTheLookDirection()
        {
            var c = Make.Fields(0f, 0f, (0, -1)); // one field north (-Z)
            ClaimGeometry.GrowField(c, 0f, 0f, Side.North, out int i, out int j);
            Assert.Equal((0, -2), (i, j));
            ClaimGeometry.GrowField(c, 0f, 0f, Side.East, out i, out j);   // east = -X
            Assert.Equal((-1, 0), (i, j));
            // standing outside: starts from the nearest field
            ClaimGeometry.GrowField(c, 40f, 0f, Side.West, out i, out j);   // west = +X
            Assert.Equal((1, 0), (i, j));
        }

        [Fact]
        public void Shrink_TakesTheOutermostFieldInTheLookDirection()
        {
            var c = Make.Fields(0f, 0f, (1, 0), (2, 0));
            Assert.True(ClaimGeometry.ShrinkField(c, 0f, 0f, Side.West, out int i, out int j));
            Assert.Equal((2, 0), (i, j));
            Assert.True(ClaimGeometry.ShrinkField(c, 0f, 30f, Side.East, out i, out j)); // from the nearest field
            Assert.Equal((0, 0), (i, j));
        }

        [Fact]
        public void Connected_And_KeepAnchorPart()
        {
            var c = Make.Fields(0f, 0f, (1, 0), (2, 0));
            Assert.True(c.IsConnected());
            c.RemoveField(1, 0);
            Assert.False(c.IsConnected());
            Assert.True(c.KeepAnchorPart());
            Assert.Single(c.Fields);
        }

        [Fact]
        public void SameOwnersClaims_MayTouch_OthersNeedTheGap()
        {
            var a = Make.Fields(0f, 0f);                       // -5..5
            var mine = Make.Fields(10f, 0f);                   // 5..15: touches
            Assert.Null(ClaimGeometry.Check(mine, new[] { a }, NoZones));
            var theirs = Make.Fields(10f, 0f);
            theirs.OwnerId = Make.Friend;
            Assert.NotNull(ClaimGeometry.Check(theirs, new[] { a }, NoZones));
            var overlapping = Make.Fields(5f, 0f);              // 0..10: overlaps
            Assert.NotNull(ClaimGeometry.Check(overlapping, new[] { a }, NoZones));
        }

        [Fact]
        public void Gap_IsMeasuredBetweenFields_NotTheRectangleAround()
        {
            var l = Make.Fields(0f, 0f, (1, 0), (2, 0), (2, 1), (2, 2)); // the corner (0,2) is empty
            var other = Make.Fields(0f, 30f);                              // field around (0,30): z 25..35
            other.OwnerId = Make.Friend;
            // the L's nearest field to it is (0,0) at z ≤ 5 → 20 m, and (2,2) x ≥ 15 vs other x ≤ 5 → 10 m
            Assert.True(l.GapTo(other) >= 10f);
            Assert.Null(ClaimGeometry.Check(other, new[] { l }, NoZones));
        }

        [Fact]
        public void Outline_L_Has6Corners_Plus_Has12_Square_Has4()
        {
            var l = Make.Fields(0f, 0f, (1, 0), (0, 1));
            Assert.Equal(6, ClaimGeometry.OutlineCorners(l.Parts()).Count);
            var plus = Make.Fields(0f, 0f, (1, 0), (-1, 0), (0, 1), (0, -1));
            Assert.Equal(12, ClaimGeometry.OutlineCorners(plus.Parts()).Count);
            var square = Make.Fields(0f, 0f, (1, 0), (0, 1), (1, 1));
            Assert.Equal(4, ClaimGeometry.OutlineCorners(square.Parts()).Count);
        }

        [Fact]
        public void Outline_TwoTouchingClaims_AreOneArea()
        {
            var a = Make.Fields(0f, 0f, (1, 0));   // x -5..15
            var b = Make.Fields(0f, 10f);          // the field north of a's anchor... (+Z): an L together
            var parts = a.Parts().Concat(b.Parts()).ToList();
            Assert.Equal(6, ClaimGeometry.OutlineCorners(parts).Count);
            var groups = ClaimGeometry.TouchingGroups(new List<Claim> { a, b });
            Assert.Single(groups);
        }

        [Fact]
        public void OutlinePoints_LeaveOutTheEdgesInside()
        {
            var c = Make.Fields(0f, 0f, (1, 0)); // x -5..15, z -5..5; the inner edge is x = 5
            var points = ClaimGeometry.OutlinePoints(c.Parts(), 1f);
            Assert.DoesNotContain(points, p => System.Math.Abs(p.X - 5f) < 0.01f && p.Z > -4.9f && p.Z < 4.9f);
            Assert.Contains(points, p => System.Math.Abs(p.X - 15f) < 0.01f);
        }

        [Fact]
        public void AbandonedFieldClaim_LosesOnlyTheFieldsInTheWay()
        {
            var c = Make.Fields(0f, 0f, (1, 0), (2, 0), (3, 0));
            // a structure at x = 30 (field 3) → that field goes, the rest stays
            Assert.True(ClaimGeometry.ShrinkToClear(c, 30f, 30f, 0f, 0f, 0f));
            Assert.Equal(3, c.Fields.Count);
            Assert.False(c.HasField(3, 0));
            // something on the anchor's own field: nothing is left
            Assert.False(ClaimGeometry.ShrinkToClear(c, 0f, 0f, 0f, 0f, 0f));
        }

        [Fact]
        public void OldRectangleClaim_BecomesFields()
        {
            var c = Make.Claim(0f, 0f, 15f); // -15..15
            c.ConvertToFields();
            Assert.Equal(9, c.Fields.Count);
            Assert.Equal(-15f, c.MinX); Assert.Equal(15f, c.MaxZ);
        }

        [Fact]
        public void AbandonedClaim_IsCutBackFromTheFields_NotTheRectangleAround()
        {
            // an active L: (0,0),(1,0),(2,0),(2,1),(2,2) – rectangle around: x -5..25, z -5..25; empty corner x -5..15, z 5..25
            var active = Make.Fields(0f, 0f, (1, 0), (2, 0), (2, 1), (2, 2));
            active.OwnerId = Make.Friend;
            // an abandoned field in that corner, exactly 10 m from every field of the L: it may stay
            var inCorner = Make.Fields(0f, 20f);            // x -5..5, z 15..25
            inCorner.State = ClaimState.Abandoned;
            Assert.True(ClaimGeometry.Apart(inCorner, active));
            Assert.True(ClaimGeometry.ShrinkToClear(inCorner, active));
            Assert.Single(inCorner.Fields);

            // closer: of an abandoned claim with fields at z 23..33 (anchor) and 13..23, only the near one goes
            var close = Make.Fields(0f, 28f, (0, -1));
            close.State = ClaimState.Abandoned;
            Assert.False(ClaimGeometry.Apart(close, active));
            Assert.True(ClaimGeometry.ShrinkToClear(close, active));
            Assert.Single(close.Fields);
            Assert.True(close.HasField(0, 0));
            Assert.True(ClaimGeometry.Apart(close, active));
        }

        [Fact]
        public void SameOwner_Apart_WhenTouching_NotWhenOverlapping()
        {
            var a = Make.Fields(0f, 0f);
            var touching = Make.Fields(10f, 0f);
            var overlapping = Make.Fields(7f, 0f);
            Assert.True(ClaimGeometry.Apart(a, touching));
            Assert.False(ClaimGeometry.Apart(a, overlapping));
            overlapping.State = ClaimState.Abandoned;
            Assert.False(ClaimGeometry.ShrinkToClear(overlapping, a)); // its only field overlaps: nothing left
        }

        [Fact]
        public void NewClaim_UsesTheGridOfTheOwnersNearestClaim()
        {
            var first = Make.Fields(0f, 0f, (1, 0));       // grid through 0/0
            var second = Make.Fields(33f, 3f);             // centred on its anchor: x 28..38
            ClaimGeometry.AlignToOwnClaims(second, new[] { first });
            Assert.Equal(30f, second.GridX);               // the field of first's grid that contains x 33
            Assert.Equal(0f, second.GridZ);
            Assert.True(second.Contains(33f, 3f));         // the anchor stays inside
            // now it can touch: growing west would reach first's field (1,0) edge at x 15... (2,0) at 20..
            var third = Make.Fields(21f, 0f);
            ClaimGeometry.AlignToOwnClaims(third, new[] { first });
            Assert.Equal(20f, third.GridX);                // field 15..25: touches first's 5..15 exactly
            Assert.Equal(0f, first.GapTo(third));
            Assert.False(first.Overlaps(third));
            // someone else's claim is not used
            var stranger = Make.Fields(52f, 1f);
            stranger.OwnerId = Make.Stranger;
            ClaimGeometry.AlignToOwnClaims(stranger, new[] { first });
            Assert.Equal(52f, stranger.GridX);
        }

        [Fact]
        public void PenaltyRest_KeepsOnlyWhatHangsTogetherWithTheAnchorInsideTheSquare()
        {
            // a U: from the anchor three fields west (+X) to (3,0), on to (3,2), back to (1,2)
            // (1,2) and (2,2) lie in the 5 x 5 square but only hang together with the anchor via (3,*) outside it
            var c = Make.Fields(0f, 0f, (1, 0), (2, 0), (3, 0), (3, 1), (3, 2), (2, 2), (1, 2));
            var rest = ClaimGeometry.PenaltyRest(c);
            Assert.True(rest.HasField(2, 0));
            Assert.False(rest.HasField(3, 0));     // outside the square
            Assert.False(rest.HasField(1, 2));     // inside the square, but cut off
            Assert.Equal(3, rest.Fields.Count);
            Assert.Equal(8, c.Fields.Count);       // the claim itself is unchanged
        }

        [Fact]
        public void PenaltySquare_Is5x5FieldsAroundTheAnchor()
        {
            var c = Make.Fields(0f, 0f);
            Assert.True(ClaimGeometry.InPenaltySquare(c, 2, -2));
            Assert.False(ClaimGeometry.InPenaltySquare(c, 3, 0));
        }

        // ------------------------------------------------------------------ slots (the example from the design)

        private static Claim Sized(int slots)
        {
            var c = Make.Fields(0f, 0f);
            for (int i = 1; i < slots * 5; i++) c.AddField(i, 0); // 50 m per slot in a row
            return c;
        }

        [Fact]
        public void Settle_DonationsAndPenalty_AsInTheExample()
        {
            // A has a claim of 4 slots: 2 own + 2 donated by B; A has 1 slot free
            var c = Sized(4);
            c.OwnSlots = 2;
            c.Donations.Add(new Donation { SteamId = Make.Friend, Slots = 2 });
            Assert.Equal(0, c.SettleSlots(1));
            Assert.Equal(2, c.OwnSlots);
            Assert.Equal(0, c.MissingSlots);

            // B leaves: A's free slot steps in, one is still missing
            c.Donations.Clear();
            Assert.Equal(1, c.SettleSlots(1));
            Assert.Equal(3, c.OwnSlots);
            Assert.Equal(1, c.MissingSlots);

            // A pays: the missing slot is bought
            c.PaidSlots += c.MissingSlots; c.MissingSlots = 0;
            c.SettleSlots(0);
            Assert.Equal(1, c.PaidSlots);
            Assert.Equal(0, c.MissingSlots);

            // C donates 1: it covers the bought slot – own + donated = 4 = needed, nothing to spare for growing
            c.Donations.Add(new Donation { SteamId = Make.Guest, Slots = 1 });
            c.SettleSlots(0);
            Assert.Equal(0, c.PaidSlots);
            Assert.Equal(3, c.OwnSlots);
            Assert.Equal(c.SlotsNeeded, c.OwnSlots + c.Donated);
        }

        [Fact]
        public void Settle_DonationReplacesOwnSlots()
        {
            var c = Sized(2);
            c.OwnSlots = 2;
            c.Donations.Add(new Donation { SteamId = Make.Friend, Slots = 1 });
            c.SettleSlots(0);
            Assert.Equal(1, c.OwnSlots); // one of the owner's slots is free again
        }

        [Fact]
        public void Settle_NeverBelowOneOwnSlot()
        {
            var c = Make.Fields(0f, 0f);
            c.Donations.Add(new Donation { SteamId = Make.Friend, Slots = 3 });
            c.SettleSlots(0);
            Assert.Equal(1, c.OwnSlots);
            Assert.Equal(0, c.MissingSlots);
        }

        [Fact]
        public void Clone_CopiesFieldsAndDonations()
        {
            var c = Make.Fields(0f, 0f, (1, 0));
            c.Donations.Add(new Donation { SteamId = Make.Friend, Slots = 1 });
            var copy = c.Clone();
            copy.AddField(2, 0);
            copy.Donations.Clear();
            Assert.Equal(2, c.Fields.Count);
            Assert.False(c.HasField(2, 0));
            Assert.Equal(1, c.Donated);
        }
    }
}
