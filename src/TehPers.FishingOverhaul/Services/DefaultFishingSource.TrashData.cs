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

                if (!trashFishIds.Contains(cleanId))
                {
                    continue;
                }

                var itemKey = this.GetFishKey(rawKey);

                if (float.TryParse(parts[10], out var chance) && int.TryParse(parts[12], out var minLevel))
                {
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
            }

            // --- STEP 2: Iterate Data/Locations for Trash ---
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

                    if (!trashFishIds.Contains(cleanId))
                    {
                        continue;
                    }

                    var itemKey = this.GetFishKey(spawnData.ItemId);

                    var info = baseAvailabilities.TryGetValue(itemKey, out var baseAvail)
                        ? baseAvail
                        : new AvailabilityInfo(0.1f);

                    var locations = this.GetLocationNames(locName);
                    info = info with { IncludeLocations = locations };

                    if (!string.IsNullOrEmpty(spawnData.Condition))
                    {
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

                        // CORRECTION : Ajout du paramètre locName ici
                        tempFishInfo = this.ParseConditionString(spawnData.Condition, tempFishInfo, locName);

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

            return new(this.manifest)
            {
                AddTrash = trashEntries.ToImmutableArray()
            };
        }
    }
}
