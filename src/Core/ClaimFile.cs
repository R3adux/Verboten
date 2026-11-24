using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Verboten.Core
{
    /// <summary>Who a mining robot belongs to (the game does not store it).</summary>
    public sealed class RobotOwner
    {
        [XmlAttribute] public long RobotId;
        [XmlAttribute] public ulong SteamId;
    }

    /// <summary>Everything Verboten keeps per world, stored next to each save file.</summary>
    [XmlRoot("Verboten")]
    public sealed class ClaimFile
    {
        public const int CurrentFormat = 2;

        [XmlAttribute] public int Format = CurrentFormat;
        /// <summary>Minutes this world has been running on the server (expiry, last seen, earned meters).</summary>
        [XmlAttribute] public long ServerMinutes;
        [XmlElement("Claim")] public List<Claim> Claims = new List<Claim>();
        [XmlElement("Player")] public List<KnownPlayer> Players = new List<KnownPlayer>();
        [XmlElement("Robot")] public List<RobotOwner> Robots = new List<RobotOwner>();

        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ClaimFile));

        /// <summary>
        /// Writes the file. Player names come from the clients: a manipulated client could use characters that
        /// are not allowed in XML, which would make the whole file unreadable (all claims off). They are removed.
        /// </summary>
        public static void Write(Stream stream, ClaimFile file)
        {
            Sanitize(file);
            Serializer.Serialize(stream, file);
        }

        public static void Write(Stream stream, IEnumerable<Claim> claims, IEnumerable<KnownPlayer> players = null)
        {
            Write(stream, new ClaimFile
            {
                Claims = new List<Claim>(claims),
                Players = players != null ? new List<KnownPlayer>(players) : new List<KnownPlayer>(),
            });
        }

        public static ClaimFile Read(Stream stream)
        {
            ClaimFile file;
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { CheckCharacters = false, CloseInput = false }))
                file = (ClaimFile)Serializer.Deserialize(reader) ?? new ClaimFile();
            if (file.Claims == null) file.Claims = new List<Claim>();
            if (file.Players == null) file.Players = new List<KnownPlayer>();
            if (file.Robots == null) file.Robots = new List<RobotOwner>();
            return file;
        }

        private static void Sanitize(ClaimFile file)
        {
            if (file.Claims != null)
                foreach (var c in file.Claims)
                {
                    if (c == null) continue;
                    c.Name = XmlSafe(c.Name);
                    c.OwnerName = XmlSafe(c.OwnerName);
                    if (c.Members != null) foreach (var m in c.Members) if (m != null) m.Name = XmlSafe(m.Name);
                    if (c.Incidents != null) foreach (var i in c.Incidents) if (i != null) i.Text = XmlSafe(i.Text);
                }
            if (file.Players != null)
                foreach (var p in file.Players)
                {
                    if (p == null) continue;
                    p.Name = XmlSafe(p.Name);
                    if (p.Notices != null)
                        for (int n = 0; n < p.Notices.Count; n++) p.Notices[n] = XmlSafe(p.Notices[n]);
                }
        }

        /// <summary>The text without characters XML cannot hold (control characters, broken surrogates).</summary>
        public static string XmlSafe(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            StringBuilder sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool pair = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                bool ok = pair || XmlConvert.IsXmlChar(c);
                if (!ok && sb == null) sb = new StringBuilder(text, 0, i, text.Length);
                if (ok && sb != null)
                {
                    sb.Append(c);
                    if (pair) sb.Append(text[i + 1]);
                }
                if (pair) i++;
            }
            return sb?.ToString() ?? text;
        }
    }
}
