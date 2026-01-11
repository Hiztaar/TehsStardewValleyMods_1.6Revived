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
        // Extending the list to include Jellies and other non-fish fishing items
        private static readonly HashSet<string> extendedTrashIds = new()
        {
            "152", // Seaweed
            "153", // Green Algae
            "157", // White Algae
            "167", // Joja Cola
            "168", // Trash
            "169", // Driftwood
            "170", // Broken Glasses
            "171", // Broken CD
            "172", // Soggy Newspaper
            "812", // Roe (Sometimes considered contextual trash)
            "856", // River Jelly (1.6)
            "857", // Sea Jelly (1.6)
            "858", // Cave Jelly (1.6)
        };

        private FishingContent GetDefaultTrashData()
        {
            var fishData = Game1.content.Load<Dictionary<string, string>>("Data\\Fish");
            var locationData = Game1.content.Load<Dictionary<string, LocationData>>("Data\\Locations");

            var trashEntries = new List<TrashEntry>();
            var baseAvailabilities = new Dictionary<NamespacedKey, AvailabilityInfo>();

            // --- STEP 1: Parse Base Trash from Data/Fish ---
            foreach (var (rawKey, data) in fishData)
            {
                var parts = data.Split('/');
                if (parts.Length < 13)
                {
                    continue;
                }

                var cleanId = rawKey.StartsWith("(O)") ? rawKey[3..] : rawKey;

                // Only keep explicit trash/algae/jellies
                if (!extendedTrashIds.Contains(cleanId))
                {
                    continue;
                }

                var itemKey = this.GetFishKey(rawKey);

                // 1.6 Reading: Clean index handling with "out var" to avoid warnings
                if (!float.TryParse(parts[10], out var chance))
                {
                    if (!float.TryParse(parts[9], out chance))
                    {
                        chance = 0.1f;
                    }
                }

                if (!int.TryParse(parts[12], out var minLevel))
                {
                    if (!int.TryParse(parts[11], out minLevel))
                    {
                        minLevel = 0;
                    }
                }

                var baseInfo = new AvailabilityInfo(chance)
                {
                    MinFishingLevel = minLevel,
                    Seasons = this.ParseSeasons(parts[6]),
                    Weathers = this.ParseWeathers(parts[7]),
                };

                var times = parts[5].Split(' ');
                baseInfo = times.Length >= 2 && int.TryParse(times[0], out var start) && int.TryParse(times[1], out var end)
                    ? baseInfo with { StartTime = start, EndTime = end }
                    : baseInfo with { StartTime = 600, EndTime = 2600 };

                baseAvailabilities[itemKey] = baseInfo;
            }

            // --- STEP 2: Iterate Data/Locations for Trash & Jellies ---
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

                    // If it is NOT a known trash item, skip (to avoid duplicating actual fish here)
                    if (!extendedTrashIds.Contains(cleanId))
                    {
                        continue;
                    }

                    var itemKey = this.GetFishKey(spawnData.ItemId);

                    // If no base info (e.g., Jellies not in Data/Fish), create default
                    var info = baseAvailabilities.TryGetValue(itemKey, out var baseAvail)
                        ? baseAvail
                        : new AvailabilityInfo(0.1f);

                    var locations = this.GetLocationNames(locName);
                    info = info with { IncludeLocations = locations };

                    if (!string.IsNullOrEmpty(spawnData.Condition))
                    {
                        // Use temporary FishAvailabilityInfo to parse the condition string
                        var tempFishInfo = new FishAvailabilityInfo(info.BaseChance)
                        {
                            StartTime = info.StartTime,
                            EndTime = info.EndTime,
                            Seasons = info.Seasons,
                            Weathers = info.Weathers,
                            MinFishingLevel = info.MinFishingLevel,
                            IncludeLocations = info.IncludeLocations,
                            When = info.When
                        };

                        // Use corrected parser (handles WATER_DEPTH etc.)
                        tempFishInfo = this.ParseConditionString(spawnData.Condition, tempFishInfo, locName);

                        // Convert back to AvailabilityInfo (Trash)
                        info = info with
                        {
                            StartTime = tempFishInfo.StartTime,
                            EndTime = tempFishInfo.EndTime,
                            Seasons = tempFishInfo.Seasons,
                            Weathers = tempFishInfo.Weathers,
                            MinFishingLevel = tempFishInfo.MinFishingLevel,
                            When = tempFishInfo.When
                        };
                    }

                    trashEntries.Add(new TrashEntry(itemKey, info));
                }
            }

            // --- STEP 3: Manual Registration for Jellies (1.6) ---

            // River Jelly: Found in Rivers and Lakes (Freshwater)
            trashEntries.Add(new TrashEntry(
                NamespacedKey.SdvObject("RiverJelly"),
                new AvailabilityInfo(0.05d)
                {
                    WaterTypes = WaterTypes.River | WaterTypes.PondOrOcean,
                    IncludeLocations = ImmutableArray.Create("Town", "Mountain", "Forest", "Desert", "Woods")
                }
            ));

            // Sea Jelly: Found in the Ocean
            trashEntries.Add(new TrashEntry(
                NamespacedKey.SdvObject("SeaJelly"),
                new AvailabilityInfo(0.05d)
                {
                    WaterTypes = WaterTypes.PondOrOcean,
                    IncludeLocations = ImmutableArray.Create("Beach", "BeachNightMarket", "IslandWest", "IslandSouth", "IslandSouthEast")
                }
            ));

            // Cave Jelly: Found in the Mines
            trashEntries.Add(new TrashEntry(
                NamespacedKey.SdvObject("CaveJelly"),
                new AvailabilityInfo(0.05d)
                {
                    WaterTypes = WaterTypes.All,
                    IncludeLocations = ImmutableArray.Create("UndergroundMine")
                }
            ));

            // --- STEP 4: Ridgeside Village Artifacts ---

            // Village Hero Sculpture
            trashEntries.Add(new TrashEntry(
                new NamespacedKey("JA", "Object/Village Hero Sculpture"),
                new AvailabilityInfo(1.0)
                {
                    IncludeLocations = ImmutableArray.Create("Custom_Ridgeside_RidgesideVillage"),
                    PriorityTier = 20,
                    FarmerPosition = new PositionConstraint
                    {
                        X = new CoordinateConstraint { GreaterThanEq = 145, LessThan = 146 },
                        Y = new CoordinateConstraint { GreaterThanEq = 69, LessThan = 70 }
                    },
                    When = ImmutableDictionary.Create<string, string?>()
                        .Add("HasFlag |contains=RSV.HeroStatue", "false")
                }
            )
            {
                OnCatch = new CatchActions
                {
                    SetFlags = ImmutableArray.Create("RSV.HeroStatue")
                }
            });

            // Sapphire Pearl
            trashEntries.Add(new TrashEntry(
                new NamespacedKey("JA", "Object/Sapphire Pearl"),
                new AvailabilityInfo(1.0)
                {
                    IncludeLocations = ImmutableArray.Create("Custom_Ridgeside_RidgesideVillage"),
                    PriorityTier = 20,
                    Position = new PositionConstraint
                    {
                        X = new CoordinateConstraint { GreaterThanEq = 60, LessThan = 61 },
                        Y = new CoordinateConstraint { GreaterThanEq = 55, LessThan = 56 }
                    },
                    When = ImmutableDictionary.Create<string, string?>()
                        .Add("HasFlag |contains=RSV.Sapphire", "false")
                }
            )
            {
                OnCatch = new CatchActions
                {
                    SetFlags = ImmutableArray.Create("RSV.Sapphire")
                }
            });

            return new(this.manifest)
            {
                AddTrash = trashEntries.ToImmutableArray()
            };
        }
    }
}
