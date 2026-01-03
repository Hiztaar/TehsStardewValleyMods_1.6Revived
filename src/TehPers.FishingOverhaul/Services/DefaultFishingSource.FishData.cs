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

            // --- ETAPE 1 : Parsing Data/Fish ---
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

                // CORRECTION CRITIQUE DES INDICES (1.6 Format)
                // 0: Nom
                // 1: Difficulté
                // 2: Comportement
                // 3: Taille Min
                // 4: Taille Max
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

                // 9/10: Chance (Float)
                if (!float.TryParse(parts[10], out var chance))
                {
                    if (!float.TryParse(parts[9], out chance))
                    {
                        chance = 0.5f;
                    }
                }

                // 11/12: Min Level (Int)
                if (!int.TryParse(parts[12], out var minLevel))
                {
                    if (!int.TryParse(parts[11], out minLevel))
                    {
                        minLevel = 0;
                    }
                }

                // 5: Heures, 6: Saisons, 7: Météo
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

            // --- ETAPE 2 : Data/Locations ---
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

                    var info = baseAvailabilities.TryGetValue(fishKey, out var baseAvail)
                        ? baseAvail
                        : new FishAvailabilityInfo(0.5f) { StartTime = 600, EndTime = 2600 };

                    var locations = this.GetLocationNames(locName);
                    info = info with { IncludeLocations = locations };

                    if (!string.IsNullOrEmpty(spawnData.Condition))
                    {
                        info = this.ParseConditionString(spawnData.Condition, info, locName);
                    }

                    // Position du joueur (Ex: Angler)
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

                    // Position du bouchon (Ex: Legend)
                    if (spawnData.BobberPosition is { } bRect)
                    {
                        info = info with
                        {
                            When = info.When.Add($"Query: BOBBER_IN_RECT {bRect.X} {bRect.Y} {bRect.Width} {bRect.Height}", "true")
                        };
                    }

                    // Restrictions Légendaires
                    if (isVanillaLegendary)
                    {
                        info = info with
                        {
                            When = info.When.Add($"Query: !PLAYER_HAS_CAUGHT_FISH Current {qualifiedId}", "true")
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

                    // Entrée Frénésie (Ignore Saisons/Heures/Météo)
                    if (!isVanillaLegendary && !isFamilyLegendary)
                    {
                        var frenzyInfo = info with
                        {
                            BaseChance = 5.0f,
                            Seasons = Seasons.Spring | Seasons.Summer | Seasons.Fall | Seasons.Winter, // Toute saison
                            Weathers = Weathers.All, // Toute météo
                            StartTime = 600, // Toute heure
                            EndTime = 2600,
                            MinFishingLevel = 0, // Pas de niveau requis
                            When = info.When.Add($"Query: CATCHING_FRENZY_FISH {qualifiedId}", "true")
                        };
                        fishEntries.Add(new FishEntry(fishKey, frenzyInfo));
                    }
                }
            }

            // --- ETAPE 3 : Injections Manuelles ---
            if (fishTraits.ContainsKey(NamespacedKey.SdvObject(158)))
            {
                var locs = new List<string>();
                for (var i = 20; i < 60; i++)
                {
                    locs.Add($"UndergroundMine/{i}");
                }
                var baseInfo = baseAvailabilities.GetValueOrDefault(NamespacedKey.SdvObject(158)) ?? new FishAvailabilityInfo(0.05f);
                fishEntries.Add(new FishEntry(NamespacedKey.SdvObject(158), baseInfo with { IncludeLocations = locs.ToImmutableArray() }));
            }

            if (fishTraits.ContainsKey(NamespacedKey.SdvObject(161)))
            {
                var locs = new List<string>();
                for (var i = 60; i < 100; i++)
                {
                    locs.Add($"UndergroundMine/{i}");
                }
                var baseInfo = baseAvailabilities.GetValueOrDefault(NamespacedKey.SdvObject(161)) ?? new FishAvailabilityInfo(0.05f);
                fishEntries.Add(new FishEntry(NamespacedKey.SdvObject(161), baseInfo with { IncludeLocations = locs.ToImmutableArray() }));
            }

            if (fishTraits.ContainsKey(NamespacedKey.SdvObject(162)))
            {
                var locs = new List<string>();
                for (var i = 100; i <= 120; i++)
                {
                    locs.Add($"UndergroundMine/{i}");
                }
                locs.Add("Caldera");
                locs.Add("VolcanoDungeon");
                var baseInfo = baseAvailabilities.GetValueOrDefault(NamespacedKey.SdvObject(162)) ?? new FishAvailabilityInfo(0.02f);
                fishEntries.Add(new FishEntry(NamespacedKey.SdvObject(162), baseInfo with { IncludeLocations = locs.ToImmutableArray() }));
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
                if (string.Equals(p, "spring", StringComparison.OrdinalIgnoreCase))
                {
                    seasons |= Seasons.Spring;
                }
                else if (string.Equals(p, "summer", StringComparison.OrdinalIgnoreCase))
                {
                    seasons |= Seasons.Summer;
                }
                else if (string.Equals(p, "fall", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "autumn", StringComparison.OrdinalIgnoreCase))
                {
                    seasons |= Seasons.Fall;
                }
                else if (string.Equals(p, "winter", StringComparison.OrdinalIgnoreCase))
                {
                    seasons |= Seasons.Winter;
                }
                else if (Enum.TryParse<Seasons>(p, true, out var s))
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

            var seasons = newSeasons != Seasons.None ? newSeasons : baseInfo.Seasons;
            var weathers = newWeather != Weathers.None ? newWeather : baseInfo.Weathers;
            var startTime = newStart ?? baseInfo.StartTime;
            var endTime = newEnd ?? baseInfo.EndTime;
            var minLevel = newLevel ?? baseInfo.MinFishingLevel;
            var locs = newLocations.Any() ? newLocations.ToImmutableArray() : baseInfo.IncludeLocations;

            return baseInfo with
            {
                Seasons = seasons,
                Weathers = weathers,
                StartTime = startTime,
                EndTime = endTime,
                MinFishingLevel = minLevel,
                IncludeLocations = locs,
                When = unparsedConditions.ToImmutableDictionary()
            };
        }
    }
}
