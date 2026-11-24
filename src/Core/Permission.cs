using System;

namespace Verboten.Core
{
    /// <summary>What a player may do inside a claim. Internal; players only see the three roles.</summary>
    [Flags]
    public enum Permission
    {
        None = 0,
        Entry = 1 << 0,      // stay inside the claim
        Use = 1 << 1,        // doors, switches, levers, valves
        Containers = 1 << 2, // take/put items in lockers, machines, slots
        Build = 1 << 3,      // construct, deconstruct, weld, cut
        Mining = 1 << 4,     // mine / place terrain
        Damage = 1 << 5,     // explosives and other damage
        Manage = 1 << 6,     // add/remove friends and guests, rename
        All = Entry | Use | Containers | Build | Mining | Damage | Manage
    }

    /// <summary>The only roles there are. Fixed on purpose: nothing to configure.</summary>
    public enum Role
    {
        None = 0,
        Guest = 1,   // may enter and use doors/switches; no building, no containers
        Friend = 2,  // may do everything except managing the claim
        Owner = 3,   // everything
    }

    public static class Roles
    {
        public static Permission Of(Role role)
        {
            switch (role)
            {
                case Role.Owner: return Permission.All;
                case Role.Friend: return Permission.All & ~Permission.Manage;
                case Role.Guest: return Permission.Entry | Permission.Use;
                default: return Permission.None;
            }
        }

        public static bool TryParse(string text, out Role role)
        {
            role = Role.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "friend": case "freund": role = Role.Friend; return true;
                case "guest": case "gast": role = Role.Guest; return true;
                default: return false;
            }
        }
    }
}
