using System.IO;
using Verboten.Core;
using Xunit;

namespace Verboten.Tests
{
    public class KnownPlayerTests
    {
        private const ulong Bob = 76561198000000001UL;
        private const ulong Eve = 76561198000000002UL;

        [Fact]
        public void FirstJoin_ThenKnown_ThenNewAddress()
        {
            var list = new KnownPlayerList();
            string home = AddressHash.Of(Bob, "1.2.3.4");
            string other = AddressHash.Of(Bob, "5.6.7.8");

            Assert.Equal(KnownPlayerList.AddressSeen.FirstTime, list.RecordAddress(Bob, home));
            Assert.Equal(KnownPlayerList.AddressSeen.Known, list.RecordAddress(Bob, home));
            Assert.Equal(KnownPlayerList.AddressSeen.NewAddress, list.RecordAddress(Bob, other));
            Assert.Equal(KnownPlayerList.AddressSeen.Known, list.RecordAddress(Bob, other));
            Assert.Equal(KnownPlayerList.AddressSeen.Known, list.RecordAddress(Bob, home));
        }

        [Fact]
        public void PlayersAreSeparate()
        {
            var list = new KnownPlayerList();
            list.RecordAddress(Bob, AddressHash.Of(Bob, "1.2.3.4"));
            Assert.Equal(KnownPlayerList.AddressSeen.FirstTime, list.RecordAddress(Eve, AddressHash.Of(Eve, "1.2.3.4")));
        }

        [Fact]
        public void KeepsOnlyRecentAddresses()
        {
            var list = new KnownPlayerList();
            for (int i = 0; i < KnownPlayer.MaxAddresses + 2; i++)
                list.RecordAddress(Bob, AddressHash.Of(Bob, $"10.0.0.{i}"));
            // the two oldest were dropped
            Assert.Equal(KnownPlayerList.AddressSeen.NewAddress, list.RecordAddress(Bob, AddressHash.Of(Bob, "10.0.0.0")));
            Assert.Single(list.CopyForSave());
            Assert.Equal(KnownPlayer.MaxAddresses, list.CopyForSave()[0].AddressHashes.Count);
        }

        [Fact]
        public void Hash_IsStable_NotTheAddress_AndPerPlayer()
        {
            string a = AddressHash.Of(Bob, "1.2.3.4");
            Assert.Equal(a, AddressHash.Of(Bob, " 1.2.3.4 "));
            Assert.DoesNotContain("1.2.3.4", a);
            Assert.NotEqual(a, AddressHash.Of(Eve, "1.2.3.4"));
            Assert.Equal(16, a.Length);
        }

        [Fact]
        public void SavedWithClaims()
        {
            var list = new KnownPlayerList();
            list.RecordAddress(Bob, AddressHash.Of(Bob, "1.2.3.4"));
            using var ms = new MemoryStream();
            ClaimFile.Write(ms, new Claim[0], list.CopyForSave());
            ms.Position = 0;
            var file = ClaimFile.Read(ms);

            var loaded = new KnownPlayerList();
            loaded.Load(file.Players);
            Assert.Equal(1, loaded.Count);
            Assert.Equal(KnownPlayerList.AddressSeen.Known, loaded.RecordAddress(Bob, AddressHash.Of(Bob, "1.2.3.4")));
        }

        [Fact]
        public void ManipulatedNames_DoNotBreakTheFile()
        {
            // a manipulated client can use characters XML cannot hold – the file must stay readable
            var list = new KnownPlayerList();
            list.Seen(Bob, "Bo\u0001b\uD800", 1);
            var claim = Make.Claim(0, 0);
            claim.OwnerName = "Evil\u0007";
            claim.AddIncident(1, "Evil\u0007 entered without permission");
            using var ms = new MemoryStream();
            ClaimFile.Write(ms, new ClaimFile { Claims = { claim }, Players = list.CopyForSave() });
            ms.Position = 0;
            var file = ClaimFile.Read(ms);
            Assert.Equal("Evil", file.Claims[0].OwnerName);
            Assert.Equal("Bob", file.Players[0].Name);
            Assert.Equal("👍 ok", ClaimFile.XmlSafe("👍 ok")); // real emoji (surrogate pairs) stay
        }

        [Fact]
        public void Pin_OnlyAsHash_PerPlayer_UpTo10Characters()
        {
            Assert.Null(Pin.Clean(""));
            Assert.Null(Pin.Clean("12345678901"));
            Assert.Equal("Abc 9!", Pin.Clean("  Abc 9!  "));
            var bob = new KnownPlayer { SteamId = Bob, PinHash = Pin.Hash(Bob, "1234") };
            Assert.True(Pin.Matches(bob, "1234"));
            Assert.False(Pin.Matches(bob, "12345"));
            Assert.NotEqual(Pin.Hash(Bob, "1234"), Pin.Hash(Bob + 1, "1234")); // same PIN, different player
            Assert.DoesNotContain("1234", bob.PinHash);
        }

        [Fact]
        public void PinAndAddresses_SurviveSaving()
        {
            var list = new KnownPlayerList();
            var home = AddressHash.Of(Bob, "1.2.3.4");
            list.RecordAddress(Bob, home);
            list.GetOrAdd(Bob).PinHash = Pin.Hash(Bob, "geheim");
            using var ms = new MemoryStream();
            ClaimFile.Write(ms, new ClaimFile { Players = list.CopyForSave() });
            ms.Position = 0;
            var loaded = new KnownPlayerList();
            loaded.Load(ClaimFile.Read(ms).Players);
            Assert.True(loaded.KnowsAddress(Bob, home));
            Assert.False(loaded.KnowsAddress(Bob, AddressHash.Of(Bob, "5.6.7.8")));
            Assert.True(Pin.Matches(loaded.Get(Bob), "geheim"));
        }

        [Fact]
        public void FindByName_ExactAndCaseInsensitive()
        {
            var list = new KnownPlayerList();
            list.Seen(Bob, "Bob", 1);
            list.Seen(Bob + 1, "Bobby", 1);
            Assert.Single(list.FindByName("bob"));
            Assert.Empty(list.FindByName("Bo"));          // no partial matches
            list.Seen(Bob + 2, "BOB", 2);
            Assert.Equal(2, list.FindByName("Bob").Count); // same name twice: the caller asks for the SteamID
        }

        [Fact]
        public void OldFileWithoutPlayers_Loads()
        {
            const string xml = "<?xml version=\"1.0\"?><Verboten Format=\"1\"><Claim Id=\"a\" HalfSize=\"5\" /></Verboten>";
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
            var file = ClaimFile.Read(ms);
            Assert.Single(file.Claims);
            Assert.Empty(file.Players);
        }
    }
}
