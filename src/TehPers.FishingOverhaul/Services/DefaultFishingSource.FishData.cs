using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Locations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TehPers.Core.Api.Gameplay;
using TehPers.Core.Api.Items;
using TehPers.FishingOverhaul.Api;
using TehPers.FishingOverhaul.Api.Content;

namespace TehPers.FishingOverhaul.Services
{
    internal sealed partial class DefaultFishingSource
    {
        private static readonly ImmutableDictionary<string, string?> legendaryBaseConditions =
            new Dictionary<string, string?>
            {
                ["TehPers.FishingOverhaul/SpecialOrderRuleActive |contains=LEGENDARY_FAMILY"] = "false",
                ["HasValue:{{HasCaughtFish}}"] = "false"
            }.ToImmutableDictionary();

        private static readonly ImmutableDictionary<string, string?> isLegendaryFamilyActive =
            new Dictionary<string, string?>
            {
                ["TehPers.FishingOverhaul/SpecialOrderRuleActive"] = "LEGENDARY_FAMILY",
            }.ToImmutableDictionary();

        private static readonly HashSet<string> legendaryFishIds = new()
        {
            "159", "160", "163", "164", "165"
        };

        private static readonly HashSet<string> legendaryFamilyIds = new()
        {
            "898", "899", "900", "901", "902"
        };

        private static readonly HashSet<string> trashFishIds = new()
        {
            "152", // Seaweed
            "153", // Green Algae
            "157"  // White Algae
        };

        // Fish IDs to handle manually via loops (to ensure Mine floors work)
        private static readonly HashSet<string> manualMineFishIds = new()
        {
            "158", // Stonefish
            "161", // Ice Pip
            "162"  // Lava Eel
        };

        private FishingContent GetDefaultFishData()
        {
            var fishData = Game1.content.Load<Dictionary<string, string>>("Data\\Fish");
            var locationData = Game1.content.Load<Dictionary<string, LocationData>>("Data\\Locations");

            var fishEntries = new List<FishEntry>();
            var fishTraits = new Dictionary<NamespacedKey, FishTraits>();
            var baseAvailabilities = new Dictionary<NamespacedKey, FishAvailabilityInfo>();

            // --- STEP 1: Parse Data/Fish ---
            foreach (var (rawKey, data) in fishData)
            {
                var parts = data.Split('/');
                if (parts.Length < 13)
                {
                    continue;
                }

                var cleanId = rawKey.StartsWith("(O)") ? rawKey[3..] : rawKey;
                if (trashFishIds.Contains(cleanId))
                {
                    continue;
                }

                var fishKey = this.GetFishKey(rawKey);

                // 1. Traits
                if (int.TryParse(parts[1], out var difficulty) &&
                    int.TryParse(parts[3], out var minSize) &&
                    int.TryParse(parts[4], out var maxSize))
                {
                    var behavior = parts[2].ToLowerInvariant() switch
                    {
                        "mixed" => DartBehavior.Mixed,
                        "dart" => DartBehavior.Dart,
                        "smooth" => DartBehavior.Smooth,
                        "sinker" => DartBehavior.Sink,
                        "sink" => DartBehavior.Sink,
                        "floater" => DartBehavior.Floater,
                        "float" => DartBehavior.Floater,
                        _ => DartBehavior.Mixed
                    };

                    var isLegendary = legendaryFishIds.Contains(cleanId)
                                      || legendaryFamilyIds.Contains(cleanId);

                    if (!isLegendary)
                    {
                        var qualifiedId = rawKey.StartsWith("(O)") ? rawKey : "(O)" + rawKey;
                        var tempItem = ItemRegistry.Create(qualifiedId);
                        if (tempItem != null && tempItem.HasContextTag("fish_legendary"))
                        {
                            isLegendary = true;
                        }
                    }

                    fishTraits[fishKey] = new FishTraits(difficulty, behavior, minSize, maxSize)
                    {
                        IsLegendary = isLegendary
                    };
                }

                // 2. Base Availability
                if (float.TryParse(parts[10], out var chance) && int.TryParse(parts[12], out var minLevel))
                {
                    var baseInfo = new FishAvailabilityInfo(chance)
                    {
                        MinFishingLevel = minLevel,
                        Seasons = this.ParseSeasons(parts[6]),
                        Weathers = this.ParseWeathers(parts[7]),
                    };

                    var times = parts[5].Split(' ');
                    baseInfo = times.Length >= 2 && int.TryParse(times[0], out var start) && int.TryParse(times[1], out var end)
                        ? baseInfo with { StartTime = start, EndTime = end }
                        : baseInfo with { StartTime = 600, EndTime = 2600 };

                    baseAvailabilities[fishKey] = baseInfo;
                }
            }

            // --- STEP 2: Iterate Data/Locations ---
            foreach (var (locName, locData) in locationData)
            {
                if (locData.Fish == null)
                {
                    continue;
                }

                foreach (var spawnData in locData.Fish)
                {
                    if (string.IsNullOrEmpty(spawnData.ItemId))
                    {
                        continue;
                    }

                    var cleanId = spawnData.ItemId.StartsWith("(O)") ? spawnData.ItemId[3..] : spawnData.ItemId;

                    if (trashFishIds.Contains(cleanId))
                    {
                        continue;
                    }

                    // SKIP Manual Mine Fish (we add them later)
                    if (manualMineFishIds.Contains(cleanId) && locName == "UndergroundMine")
                    {
                        continue;
                    }

                    var fishKey = this.GetFishKey(spawnData.ItemId);
                    if (!fishTraits.ContainsKey(fishKey))
                    {
                        continue;
                    }

                    var info = baseAvailabilities.TryGetValue(fishKey, out var baseAvail)
                        ? baseAvail
                        : new FishAvailabilityInfo(0.5f) { StartTime = 600, EndTime = 2600 };

                    var locations = this.GetLocationNames(locName);
                    info = info with { IncludeLocations = locations };

                    if (!string.IsNullOrEmpty(spawnData.Condition))
                    {
                        info = this.ParseConditionString(spawnData.Condition, info, locName);
                    }

                    if (legendaryFishIds.Contains(cleanId))
                    {
                        info = info with { When = legendaryBaseConditions };
                    }
                    else if (legendaryFamilyIds.Contains(cleanId))
                    {
                        info = info with { When = isLegendaryFamilyActive };
                    }

                    fishEntries.Add(new FishEntry(fishKey, info));
                }
            }

            // --- STEP 3: Manual Injections (Fix Mine Fish) ---
            // We manually loop through floors to ensure availability, bypassing parser issues.

            // 158: Stonefish (Floors 20-60)
            if (fishTraits.ContainsKey(NamespacedKey.SdvObject(158)))
            {
                var locs = new List<string>();
                for (var i = 20; i < 60; i++)
                {
                    locs.Add($"UndergroundMine/{i}");
                }

                var baseInfo = baseAvailabilities.ContainsKey(NamespacedKey.SdvObject(158))
                    ? baseAvailabilities[NamespacedKey.SdvObject(158)]
                    : new FishAvailabilityInfo(0.05f);

                fishEntries.Add(new FishEntry(NamespacedKey.SdvObject(158), baseInfo with { IncludeLocations = locs.ToImmutableArray() }));
            }

            // 161: Ice Pip (Floors 60-100)
            if (fishTraits.ContainsKey(NamespacedKey.SdvObject(161)))
            {
                var locs = new List<string>();
                for (var i = 60; i < 100; i++)
                {
                    locs.Add($"UndergroundMine/{i}");
                }

                var baseInfo = baseAvailabilities.ContainsKey(NamespacedKey.SdvObject(161))
                    ? baseAvailabilities[NamespacedKey.SdvObject(161)]
                    : new FishAvailabilityInfo(0.05f);

                fishEntries.Add(new FishEntry(NamespacedKey.SdvObject(161), baseInfo with { IncludeLocations = locs.ToImmutableArray() }));
            }

            // 162: Lava Eel (Floors 100-120) + Caldera
            if (fishTraits.ContainsKey(NamespacedKey.SdvObject(162)))
            {
                var locs = new List<string>();
                for (var i = 100; i <= 120; i++)
                {
                    locs.Add($"UndergroundMine/{i}");
                }
                locs.Add("Caldera"); // Volcano

                var baseInfo = baseAvailabilities.ContainsKey(NamespacedKey.SdvObject(162))
                    ? baseAvailabilities[NamespacedKey.SdvObject(162)]
                    : new FishAvailabilityInfo(0.02f);

                fishEntries.Add(new FishEntry(NamespacedKey.SdvObject(162), baseInfo with { IncludeLocations = locs.ToImmutableArray() }));
            }

            return new(this.manifest)
            {
                AddFish = fishEntries.ToImmutableArray(),
                SetFishTraits = fishTraits.ToImmutableDictionary()
            };
        }

        private NamespacedKey GetFishKey(string rawId)
        {
            var cleanId = rawId.StartsWith("(O)") ? rawId[3..] : rawId;
            if (int.TryParse(cleanId, out var intId))
            {
                return NamespacedKey.SdvObject(intId);
            }
            return NamespacedKey.SdvObject(cleanId);
        }

        private ImmutableArray<string> GetLocationNames(string locationName)
        {
            return locationName switch
            {
                "Beach" => ImmutableArray.Create("Beach", "BeachNightMarket", "Farm/Beach"),
                "Forest" => ImmutableArray.Create("Forest", "Farm/Riverland", "Farm/Forest", "Farm/Hills", "Farm/FourCorners"),
                "Town" => ImmutableArray.Create("Town", "Farm/Riverland", "Farm/Standard"),
                "Mountain" => ImmutableArray.Create("Mountain", "Farm/Mountain", "Farm/FourCorners", "Farm/Wilderness"),
                "UndergroundMine" => ImmutableArray.Create("UndergroundMine"),
                _ => ImmutableArray.Create(locationName)
            };
        }

        private Seasons ParseSeasons(string data)
        {
            var seasons = Seasons.None;
            var parts = data.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (Enum.TryParse<Seasons>(p, true, out var s))
                {
                    seasons |= s;
                }
            }
            return seasons == Seasons.None ? (Seasons.Spring | Seasons.Summer | Seasons.Fall | Seasons.Winter) : seasons;
        }

        private Weathers ParseWeathers(string data)
        {
            var w = Weathers.None;
            if (data.Contains("sunny", StringComparison.OrdinalIgnoreCase))
            {
                w |= Weathers.Sunny;
            }
            if (data.Contains("rainy", StringComparison.OrdinalIgnoreCase))
            {
                w |= Weathers.Rainy;
            }
            if (data.Contains("both", StringComparison.OrdinalIgnoreCase))
            {
                w = Weathers.All;
            }
            return w == Weathers.None ? Weathers.All : w;
        }

        private FishAvailabilityInfo ParseConditionString(string condition, FishAvailabilityInfo baseInfo, string locationName)
        {
            var conditions = condition.Split(new[] { '/', ',' }, StringSplitOptions.RemoveEmptyEntries);

            var newSeasons = Seasons.None;
            var newWeather = Weathers.None;
            var newLocations = new List<string>();
            var unparsedConditions = new Dictionary<string, string?>();

            var newStart = (int?)null;
            var newEnd = (int?)null;
            var newLevel = (int?)null;

            foreach (var cond in conditions)
            {
                var parts = cond.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                var key = parts[0].ToUpperInvariant();

                switch (key)
                {
                    case "SEASON":
                    case "LOCATION_SEASON":
                        for (var i = 1; i < parts.Length; i++)
                        {
                            if (Enum.TryParse<Seasons>(parts[i], true, out var s))
                            {
                                newSeasons |= s;
                            }
                        }
                        break;

                    case "WEATHER":
                        for (var i = 1; i < parts.Length; i++)
                        {
                            if (parts[i].Equals("rain", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("storm", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("snow", StringComparison.OrdinalIgnoreCase))
                            {
                                newWeather |= Weathers.Rainy;
                            }
                            else if (parts[i].Equals("sun", StringComparison.OrdinalIgnoreCase))
                            {
                                newWeather |= Weathers.Sunny;
                            }
                        }
                        break;

                    case "TIME":
                        if (parts.Length >= 3 && int.TryParse(parts[1], out var sTime) && int.TryParse(parts[2], out var eTime))
                        {
                            newStart = sTime;
                            newEnd = eTime;
                        }
                        break;

                    case "FISHING_LEVEL":
                        if (parts.Length >= 2 && int.TryParse(parts[1], out var lvl))
                        {
                            newLevel = lvl;
                        }
                        break;

                    case "MINE_LEVEL":
                        // Keep this for other mine fish (like Ghostfish or modded ones)
                        if (parts.Length >= 2 && int.TryParse(parts[1], out var startLevel))
                        {
                            var endLevel = startLevel;
                            if (parts.Length >= 3 && int.TryParse(parts[2], out var parsedEnd))
                            {
                                endLevel = parsedEnd;
                            }

                            for (var i = startLevel; i <= endLevel; i++)
                            {
                                newLocations.Add($"{locationName}/{i}");
                            }
                        }
                        break;

                    case "IS_PASSIVE_FESTIVAL_OPEN":
                    case "IS_FESTIVAL_DAY":
                    case "PLAYER_SPECIAL_ORDER_RULE_ACTIVE":
                    case "!PLAYER_SPECIAL_ORDER_RULE_ACTIVE":
                    case "RANDOM":
                        break;

                    default:
                        if (cond.Contains("HasFlag") || cond.Contains("TehPers"))
                        {
                            unparsedConditions[$"Query: {cond.Trim()}"] = "true";
                        }
                        break;
                }
            }

            return baseInfo with
            {
                Seasons = newSeasons != Seasons.None ? newSeasons : baseInfo.Seasons,
                Weathers = newWeather != Weathers.None ? newWeather : baseInfo.Weathers,
                StartTime = newStart ?? baseInfo.StartTime,
                EndTime = newEnd ?? baseInfo.EndTime,
                MinFishingLevel = newLevel ?? baseInfo.MinFishingLevel,
                IncludeLocations = newLocations.Any() ? newLocations.ToImmutableArray() : baseInfo.IncludeLocations,
                When = unparsedConditions.ToImmutableDictionary()
            };
        }
    }
}
