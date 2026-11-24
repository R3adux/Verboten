# Verboten – Base Claims & Anti-Griefing for Stationeers

**Build an autolathe. Your base is protected.** A server-side mod: only the host or dedicated server needs it
(BepInEx 5 + StationeersLaunchPad). Players join with the unmodified game.

Version 0.2.0 – early. Tested in the game by the host; protection against other players has not run with a
second player yet. See *Status* below.

## What it does

- **Claims made of fields.** The first autolathe a player builds protects a 10 x 10 m field around it, with no
  limit up or down. `/claim grow` adds the next 10 x 10 m field in the direction the player looks – any connected
  shape.
- **Claim slots.** Every player has 3 slots (setting). A claim needs one per started 50 m in each direction, so a
  player can have three small bases or one big one. A player's own claims may touch; other players' claims keep
  10 m apart. The landing zone (spawn radius + 20 m) can't be claimed.
- **Area accounts.** Every player starts with 300 m² and earns 25 m² per hour online while they own or donate to
  a claim. A field costs 100 m²: from the owner's account, then the donors', then steel from the claim's
  autolathe (the price is shown and confirmed first).
- **Donations.** Friends donate slots to a claim (`/claim donate <n>`), so a group can build big. If a donation
  is taken back and the owner's own slots don't cover the claim, the owner pays a penalty (`/claim pay`) or the
  claim shrinks to 50 x 50 m around its autolathe after 3 hours of their play time.
- **Roles.** Owner, friends (everything except managing people), guests (come in, use doors and switches).
- **Protection.** Building, deconstructing, devices, containers, pipes, cables, rooms and gases, mining,
  weapons and explosions. Strangers are warned in the 10 m zone around a claim and disconnected after 3 seconds
  inside; the third time in 30 minutes locks them out for 30 minutes. Strangers' robots stay out.
- **Abandoned bases.** Claims whose owner and friends are away for 7 days (server time) become abandoned, get a
  tracker beacon and can be taken over; they give way to active players.
- **Hard to cheat.** Every network message is tied to the connection that really sent it. Creative and spawn
  cheats from players are blocked in survival. Players on IP connections (all players on a dedicated server) can
  set a PIN against faked identities.
- **Borders shown in the world** with flares at the outer corners (or sparks), help in four windows
  (`/claim help 1–4`, with a site plan of your claim), English and German texts.

## Commands

| Command | |
|---|---|
| `/claim help [1–4]` | everything, with your own claim first |
| `/claim check` | may I protect my base here? |
| `/claim show` | borders and landing zones nearby for 30 s |
| `/claim grow` | one more field where you look |
| `/claim add <player> [guest]` / `remove <player>` | friends and guests |
| `/claim donate <n>` / `pay` | donate slots / pay a penalty |
| `/claim info` · `find` · `take` · `name` · `owner` · `delete` · `pin` | see `/claim help 4` |

Admins (host, `AdminSteamIds`, or `serverrun claim …` on dedicated servers): `list`, `info <claim>`, `shrink`,
`delete <claim>`, `pin off <player>`, `status` (which game hooks are installed).

## Install and settings

Subscribe on the host or put the mod folder into the mods folder used by StationeersLaunchPad. Settings are in
`BepInEx/config/verboten.stationeers.cfg` on the machine that hosts (language, slots, start area, earned area,
prices, expiry, landing zone building, border markers, admins). Players' own copies have no effect on a server.

## Status

- Logic: 240 unit tests, including checks against the game's DLL that every hooked method still exists.
- In the game (host only): claims, commands, windows, flares and sparks work.
- **Not yet run with a second player:** the protection itself, entering/kicking, PIN, donations, penalty, fields.

The rules and their reasons are in [docs/DESIGN.md](docs/DESIGN.md) (German; section 0, newest subsections last).

## Building

`dotnet build -c Release` (needs the game's managed DLLs; see `Verboten.csproj`), tests: `dotnet test tests/Verboten.Tests`.

MIT licence.
