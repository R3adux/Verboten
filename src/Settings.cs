using BepInEx.Configuration;

namespace Verboten
{
    public enum ClaimAnchor
    {
        Autolathe,
        Flag,
    }

    public enum BorderMarker
    {
        Flares,
        Sparks,
    }

    public enum Language
    {
        English,
        Deutsch,
    }

    /// <summary>All settings in one place. Kept deliberately short.</summary>
    public sealed class Settings
    {
        public readonly ConfigEntry<Language> Language;
        public readonly ConfigEntry<ClaimAnchor> Anchor;
        public readonly ConfigEntry<bool> GrowingEnabled;
        public readonly ConfigEntry<int> StartFreeArea;
        public readonly ConfigEntry<int> ClaimSlotsPerPlayer;
        public readonly ConfigEntry<int> ClaimExpiryDays;
        public readonly ConfigEntry<bool> BuildingInLandingZone;
        public readonly ConfigEntry<float> EarnedAreaPerHour;
        public readonly ConfigEntry<BorderMarker> BorderMarkers;
        public readonly ConfigEntry<float> SteelPerSquareMeter;
        public readonly ConfigEntry<float> PriceIncreasePer100SquareMeters;
        public readonly ConfigEntry<string> AdminSteamIds;

        public Settings(ConfigFile config)
        {
            Language = config.Bind("General", "Language", Verboten.Language.English,
                "Language of everything players read (chat lines, windows): English or Deutsch. The log stays English.");
            Anchor = config.Bind("Claims", "Anchor", ClaimAnchor.Autolathe,
                "What creates a claim. Autolathe: the first autolathe a player builds. Flag: players stake out their claim with a flag at each corner (4 flags; the outermost ones are the border, 10 x 10 to 50 x 50 m; the Growing settings do not apply).");
            ClaimSlotsPerPlayer = config.Bind("Claims", "ClaimSlotsPerPlayer", 3,
                "Claim slots per player. One slot covers up to 50 x 50 m: a player can own that many claims, or fewer bigger ones " +
                "(a claim needs one slot per started 50 m in each direction, e.g. 100 x 50 m = 2). Friends can donate their slots to a claim (/claim donate).");
            ClaimExpiryDays = config.Bind("Claims", "ClaimExpiryDays", 7,
                "Days (counted only while the server runs) without the owner or a friend online before a claim is abandoned.");
            BuildingInLandingZone = config.Bind("Claims", "BuildingInLandingZone", true,
                "true: everyone may build at the landing zone (spawn radius + 20 m; nobody can claim it). " +
                "false: only admins can build or take things apart there.");
            GrowingEnabled = config.Bind("Growing", "Enabled", true,
                "true: a new claim is one 10 x 10 m field and owners add fields themselves (paid with their account: start credit and " +
                "area earned by playing, then steel). false: a claim covers the base around its anchor (everything the owner and friends built, " +
                "plus 5 m) and grows by itself as they build on – up to 50 x 50 m. The settings below only matter when this is true.");
            StartFreeArea = config.Bind("Growing", "StartFreeArea", 300,
                "Square meters every player gets once on their account (for growing their claims or the ones they donate to).");
            EarnedAreaPerHour = config.Bind("Growing", "EarnedAreaPerHour", 25f,
                "Square meters a player earns on their account for every hour online while they own or donate to a claim.");
            SteelPerSquareMeter = config.Bind("Growing", "SteelPerSquareMeter", 0.5f,
                "Steel (as shown in the autolathe) per square meter when growing a claim beyond the area on the accounts.");
            PriceIncreasePer100SquareMeters = config.Bind("Growing", "PriceIncreasePer100SquareMeters", 0.05f,
                "How much the price per square meter rises per 100 m² of claim area (0.05 = +5 %).");
            BorderMarkers = config.Bind("General", "BorderMarkers", BorderMarker.Flares,
                "How /claim show and new claims mark borders. Flares: burning flares at the corners for 30 s (grey: your own claim, " +
                "khaki: other claims, red with yellow at the compass points: landing zone) – everyone nearby sees them. Sparks: sparks along the borders, only for that player.");
            AdminSteamIds = config.Bind("Admins", "AdminSteamIds", "",
                "Comma-separated SteamID64 list of admins (the host is always an admin).");
        }
    }
}
