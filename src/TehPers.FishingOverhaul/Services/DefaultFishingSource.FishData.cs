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
        private static readonly HashSet<string> legendaryFishIds = new()
        {
            "159", "160", "163", "682", "775"
        };

        private static readonly HashSet<string> legendaryFamilyIds = new()
        {
            "898", "899", "900", "901", "902"
        };

        private static readonly HashSet<string> trashFishIds = new()
        {
            "152", "153", "157"
        };

        private static readonly HashSet<string> manualOverrideIds = new()
        {
            "158", "161", "162", "164", "165", "798", "799", "800"
        };

        private FishingContent GetDefaultFishData()
        {
            var fishData = Game1.content.Load<Dictionary<string, string>>("Data\\Fish");
            var locationData = Game1.content.Load<Dictionary<string, LocationData>>("Data\\Locations");

            var fishEntries = new List<FishEntry>();
            var fishTraits = new Dictionary<NamespacedKey, FishTraits>();
            var baseAvailabilities = new Dictionary<NamespacedKey, FishAvailabilityInfo>();

            // --- STEP 1: Parsing Data/Fish ---
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
                var qualifiedId = rawKey.StartsWith("(O)") ? rawKey : "(O)" + rawKey;
                var tempItem = ItemRegistry.Create(qualifiedId);

                var isFamilyLegendary = (tempItem != null && tempItem.HasContextTag("fish_legendary_family")) || legendaryFamilyIds.Contains(cleanId);
                var isVanillaLegendary = !isFamilyLegendary && ((tempItem != null && tempItem.HasContextTag("fish_legendary")) || legendaryFishIds.Contains(cleanId));
                var isLegendary = isVanillaLegendary || isFamilyLegendary;

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

                    fishTraits[fishKey] = new FishTraits(difficulty, behavior, minSize, maxSize)
                    {
                        IsLegendary = isLegendary
                    };
                }

                if (!float.TryParse(parts[10], out var chance))
                {
                    if (!float.TryParse(parts[9], out chance))
                    {
                        chance = 0.5f;
                    }
                }

                if (!int.TryParse(parts[12], out var minLevel))
                {
                    if (!int.TryParse(parts[11], out minLevel))
                    {
                        minLevel = 0;
                    }
                }

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
                    if (trashFishIds.Contains(cleanId) || manualOverrideIds.Contains(cleanId))
                    {
                        continue;
                    }

                    var fishKey = this.GetFishKey(spawnData.ItemId);
                    if (!fishTraits.ContainsKey(fishKey))
                    {
                        continue;
                    }

                    var qualifiedId = spawnData.ItemId.StartsWith("(O)") ? spawnData.ItemId : "(O)" + spawnData.ItemId;
                    var tempItem = ItemRegistry.Create(qualifiedId);

                    var isFamilyLegendary = (tempItem != null && tempItem.HasContextTag("fish_legendary_family")) || legendaryFamilyIds.Contains(cleanId);
                    var isVanillaLegendary = !isFamilyLegendary && ((tempItem != null && tempItem.HasContextTag("fish_legendary")) || legendaryFishIds.Contains(cleanId));
                    var isLegendary = isVanillaLegendary || isFamilyLegendary;

                    var info = baseAvailabilities.TryGetValue(fishKey, out var baseAvail)
                        ? baseAvail
                        : new FishAvailabilityInfo(0.5f) { StartTime = 600, EndTime = 2600 };

                    var locations = this.GetLocationNames(locName, isLegendary);
                    info = info with { IncludeLocations = locations };

                    // --- STRICT WATER TYPE ENFORCEMENT ---
                    var waterConstraint = locName switch
                    {
                        "Beach" => WaterTypes.PondOrOcean,
                        "Town" => WaterTypes.River,
                        "Forest" => WaterTypes.River | WaterTypes.Freshwater,
                        "Mountain" => WaterTypes.Freshwater,
                        "Desert" => WaterTypes.Freshwater,
                        _ => WaterTypes.All
                    };

                    if (waterConstraint != WaterTypes.All)
                    {
                        info = info with { WaterTypes = waterConstraint };
                    }

                    if (!string.IsNullOrEmpty(spawnData.Condition))
                    {
                        info = this.ParseConditionString(spawnData.Condition, info, locName);
                    }

                    if (spawnData.PlayerPosition is { } pRect)
                    {
                        var xMax = pRect.X + pRect.Width;
                        var yMax = pRect.Y + pRect.Height;
                        info = info with
                        {
                            When = info.When
                                .Add($"Query: PLAYER_TILE_X Current {pRect.X} {xMax}", "true")
                                .Add($"Query: PLAYER_TILE_Y Current {pRect.Y} {yMax}", "true")
                        };
                    }

                    if (spawnData.BobberPosition is { } bRect)
                    {
                        info = info with
                        {
                            When = info.When.Add($"Query: BOBBER_IN_RECT {bRect.X} {bRect.Y} {bRect.Width} {bRect.Height}", "true")
                        };
                    }

                    if (isVanillaLegendary)
                    {
                        var cleanedWhen = info.When.Where(pair => !pair.Key.Contains("!PLAYER_HAS_CAUGHT_FISH") && !pair.Key.Contains("!PLAYER_HAS_CAUGHT_FISH_AT_LOCATION"));
                        info = info with { When = cleanedWhen.ToImmutableDictionary() };
                        info = info with
                        {
                            When = info.When.Add($"Query: LEGENDARY_IS_RECHARGEABLE Current {qualifiedId}", "true")
                        };
                    }
                    else if (isFamilyLegendary)
                    {
                        info = info with
                        {
                            When = info.When.Add("Query: PLAYER_HAS_SPECIAL_ORDER_RULE Current LEGENDARY_FAMILY", "true")
                        };
                    }

                    fishEntries.Add(new FishEntry(fishKey, info));

                    if (!isVanillaLegendary && !isFamilyLegendary)
                    {
                        var frenzyInfo = info with
                        {
                            BaseChance = 5.0f,
                            Seasons = Seasons.Spring | Seasons.Summer | Seasons.Fall | Seasons.Winter,
                            Weathers = Weathers.All,
                            StartTime = 600,
                            EndTime = 2600,
                            MinFishingLevel = 0,
                            When = info.When.Add($"Query: CATCHING_FRENZY_FISH {qualifiedId}", "true")
                        };
                        fishEntries.Add(new FishEntry(fishKey, frenzyInfo));
                    }
                }
            }

            // --- STEP 3: Manual Injections ---
            if (fishTraits.ContainsKey(NamespacedKey.SdvObject(158)))
            {
                var locs = Enumerable.Range(20, 40).Select(i => $"UndergroundMine/{i}").ToImmutableArray();
                var baseInfo = baseAvailabilities.GetValueOrDefault(NamespacedKey.SdvObject(158)) ?? new FishAvailabilityInfo(0.05f);
                fishEntries.Add(new FishEntry(NamespacedKey.SdvObject(158), baseInfo with { IncludeLocations = locs }));
            }

            if (fishTraits.ContainsKey(NamespacedKey.SdvObject(161)))
            {
                var locs = Enumerable.Range(60, 40).Select(i => $"UndergroundMine/{i}").ToImmutableArray();
                var baseInfo = baseAvailabilities.GetValueOrDefault(NamespacedKey.SdvObject(161)) ?? new FishAvailabilityInfo(0.05f);
                fishEntries.Add(new FishEntry(NamespacedKey.SdvObject(161), baseInfo with { IncludeLocations = locs }));
            }

            if (fishTraits.ContainsKey(NamespacedKey.SdvObject(162)))
            {
                var locs = Enumerable.Range(100, 21).Select(i => $"UndergroundMine/{i}").Concat(new[] { "Caldera", "VolcanoDungeon" }).ToImmutableArray();
                var baseInfo = baseAvailabilities.GetValueOrDefault(NamespacedKey.SdvObject(162)) ?? new FishAvailabilityInfo(0.02f);
                fishEntries.Add(new FishEntry(NamespacedKey.SdvObject(162), baseInfo with { IncludeLocations = locs }));
            }

            var isSpring = Game1.currentSeason.Equals("spring", StringComparison.OrdinalIgnoreCase);
            var isFestivalDay = Game1.dayOfMonth is >= 15 and <= 17;
            var isDesertFestival = isSpring && isFestivalDay;

            if (!isDesertFestival)
            {
                foreach (var id in new[] { 164, 165 })
                {
                    if (fishTraits.ContainsKey(NamespacedKey.SdvObject(id)))
                    {
                        var baseInfo = baseAvailabilities.GetValueOrDefault(NamespacedKey.SdvObject(id)) ?? new FishAvailabilityInfo(0.1f);
                        fishEntries.Add(new FishEntry(NamespacedKey.SdvObject(id), baseInfo with { IncludeLocations = ImmutableArray.Create("Desert") }));
                    }
                }
            }

            foreach (var id in new[] { 798, 799, 800, 154, 155, 149 })
            {
                if (fishTraits.ContainsKey(NamespacedKey.SdvObject(id)))
                {
                    var baseInfo = baseAvailabilities.GetValueOrDefault(NamespacedKey.SdvObject(id)) ?? new FishAvailabilityInfo(0.1f);
                    fishEntries.Add(new FishEntry(NamespacedKey.SdvObject(id), baseInfo with { IncludeLocations = ImmutableArray.Create("Submarine") }));
                }
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

        // THIS IS THE SINGLE SOURCE OF TRUTH FOR THIS METHOD
        private ImmutableArray<string> GetLocationNames(string locationName, bool isLegendary = false)
        {
            if (isLegendary)
            {
                return ImmutableArray.Create(locationName);
            }

            // Separate lists for Frontier Farm (River Only) vs Ferngill (River + Beach)
            var frontierFarmOnly = new[] { "Custom_FrontierFarm", "FrontierFarm" };
            var ferngillMulti = new[] { "Custom_FerngillRepublicFrontier", "Custom_Ferngill_Frontier", "Ferngill_Frontier", "Custom_FerngillFrontier" };
            var standardFarm = new[] { "Farm" };

            return locationName switch
            {
                "Beach" => ImmutableArray.Create("Beach", "BeachNightMarket", "Farm/Beach")
                    .AddRange(ferngillMulti)
                    .AddRange(standardFarm),

                "Forest" => ImmutableArray.Create("Forest", "Farm/Riverland", "Farm/Forest", "Farm/Hills", "Farm/FourCorners")
                    .AddRange(frontierFarmOnly)
                    .AddRange(ferngillMulti)
                    .AddRange(standardFarm),

                "Town" => ImmutableArray.Create("Town", "Farm/Riverland", "Farm/Standard")
                    .AddRange(frontierFarmOnly)
                    .AddRange(ferngillMulti)
                    .AddRange(standardFarm),

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
                else if (p.Equals("spring", StringComparison.OrdinalIgnoreCase))
                {
                    seasons |= Seasons.Spring;
                }
                else if (p.Equals("summer", StringComparison.OrdinalIgnoreCase))
                {
                    seasons |= Seasons.Summer;
                }
                else if (p.Equals("fall", StringComparison.OrdinalIgnoreCase) || p.Equals("autumn", StringComparison.OrdinalIgnoreCase))
                {
                    seasons |= Seasons.Fall;
                }
                else if (p.Equals("winter", StringComparison.OrdinalIgnoreCase))
                {
                    seasons |= Seasons.Winter;
                }
            }
            return seasons == Seasons.None ? Seasons.All : seasons;
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
            int? newStart = null, newEnd = null, newLevel = null;

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
                    default:
                        if (!cond.Contains("!PLAYER_HAS_CAUGHT_FISH"))
                        {
                            unparsedConditions[$"Query: {cond.Trim()}"] = "true";
                        }
                        break;
                }
            }
            return baseInfo with { Seasons = newSeasons != Seasons.None ? newSeasons : baseInfo.Seasons, Weathers = newWeather != Weathers.None ? newWeather : baseInfo.Weathers, StartTime = newStart ?? baseInfo.StartTime, EndTime = newEnd ?? baseInfo.EndTime, MinFishingLevel = newLevel ?? baseInfo.MinFishingLevel, IncludeLocations = newLocations.Any() ? newLocations.ToImmutableArray() : baseInfo.IncludeLocations, When = unparsedConditions.ToImmutableDictionary() };
        }
    }
}
