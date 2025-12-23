using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Locations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using TehPers.Core.Api.Gameplay;
using TehPers.Core.Api.Items;
using TehPers.FishingOverhaul.Api.Content;

namespace TehPers.FishingOverhaul.Services
{
    internal sealed partial class DefaultFishingSource
    {
        private static readonly ImmutableDictionary<string, string?> hasCaughtFish =
            new Dictionary<string, string?>
            {
                ["HasValue:{{HasCaughtFish}}"] = "true",
            }.ToImmutableDictionary();

        private static readonly ImmutableDictionary<string, string?> legendaryBaseConditions =
            DefaultFishingSource.hasCaughtFish.Add(
                "TehPers.FishingOverhaul/SpecialOrderRuleActive |contains=LEGENDARY_FAMILY",
                "false"
            );

        private static readonly ImmutableDictionary<string, string?> isLegendaryFamilyActive =
            new Dictionary<string, string?>
            {
                ["TehPers.FishingOverhaul/SpecialOrderRuleActive"] = "LEGENDARY_FAMILY",
            }.ToImmutableDictionary();

        private static readonly NamespacedKey crimsonfishKey = NamespacedKey.SdvObject(159);
        private static readonly NamespacedKey anglerKey = NamespacedKey.SdvObject(160);
        private static readonly NamespacedKey legendKey = NamespacedKey.SdvObject(163);
        private static readonly NamespacedKey glacierfishKey = NamespacedKey.SdvObject(164);
        private static readonly NamespacedKey mutantCarpKey = NamespacedKey.SdvObject(165);

        private static readonly NamespacedKey sonOfCrimsonfishKey = NamespacedKey.SdvObject(898);
        private static readonly NamespacedKey msAnglerKey = NamespacedKey.SdvObject(899);
        private static readonly NamespacedKey legendIiKey = NamespacedKey.SdvObject(900);
        private static readonly NamespacedKey glacierfishJrKey = NamespacedKey.SdvObject(901);
        private static readonly NamespacedKey radioactiveCarpKey = NamespacedKey.SdvObject(902);

        [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Required by signature")]
        private FishingContent GetDefaultFishData(IMonitor monitor)
        {
            var fishData = Game1.content.Load<Dictionary<string, string>>("Data\\Fish");
            var locationData = Game1.content.Load<Dictionary<string, LocationData>>("Data\\Locations");

            var fishLocations = new Dictionary<string, HashSet<string>>();
            foreach (var kvp in locationData)
            {
                var locationName = kvp.Key;
                var locData = kvp.Value;
                if (locData.Fish == null)
                {
                    continue;
                }

                foreach (var fishSpawn in locData.Fish)
                {
                    if (fishSpawn.ItemId == null)
                    {
                        continue;
                    }

                    var rawId = fishSpawn.ItemId;
                    if (!fishLocations.TryGetValue(rawId, out var locs))
                    {
                        locs = new HashSet<string>();
                        fishLocations[rawId] = locs;
                    }
                    locs.Add(locationName);

                    if (rawId.StartsWith("(O)"))
                    {
                        var bareId = rawId[3..];
                        if (!fishLocations.TryGetValue(bareId, out var locsBare))
                        {
                            locsBare = new HashSet<string>();
                            fishLocations[bareId] = locsBare;
                        }
                        locsBare.Add(locationName);
                    }
                }
            }

            var fishEntries = new List<FishEntry>();

            foreach (var (key, data) in fishData)
            {
                var fishInfo = data.Split('/');
                if (fishInfo.Length < 13)
                {
                    continue;
                }

                if (!ItemRegistry.Exists("(O)" + key) && !int.TryParse(key, out _))
                {
                    continue;
                }

                var itemKey = int.TryParse(key, out var numericId)
                    ? NamespacedKey.SdvObject(numericId)
                    : NamespacedKey.SdvObject(key);

                var validLocations = ImmutableArray<string>.Empty;
                if (fishLocations.TryGetValue(key, out var foundLocs))
                {
                    validLocations = foundLocs.ToImmutableArray();
                }

                if (this.TryParseFish(fishInfo, out var availabilities))
                {
                    foreach (var availability in availabilities)
                    {
                        // Correction IDE0045: Simplification
                        var finalAvailability = !validLocations.IsEmpty
                            ? availability with { IncludeLocations = validLocations }
                            : availability with { IncludeLocations = ImmutableArray.Create("__NONE__") };

                        var entry = new FishEntry(itemKey, finalAvailability);

                        if (itemKey.Equals(DefaultFishingSource.crimsonfishKey))
                        {
                            this.ApplyLegendaryLogic(ref entry);
                        }
                        else if (itemKey.Equals(DefaultFishingSource.anglerKey))
                        {
                            this.ApplyLegendaryLogic(ref entry);
                        }
                        else if (itemKey.Equals(DefaultFishingSource.legendKey))
                        {
                            this.ApplyLegendaryLogic(ref entry);
                        }
                        else if (itemKey.Equals(DefaultFishingSource.glacierfishKey))
                        {
                            this.ApplyLegendaryLogic(ref entry);
                        }
                        else if (itemKey.Equals(DefaultFishingSource.mutantCarpKey))
                        {
                            this.ApplyLegendaryLogic(ref entry);
                        }
                        else if (itemKey.Equals(DefaultFishingSource.sonOfCrimsonfishKey) ||
                                 itemKey.Equals(DefaultFishingSource.msAnglerKey) ||
                                 itemKey.Equals(DefaultFishingSource.legendIiKey) ||
                                 itemKey.Equals(DefaultFishingSource.glacierfishJrKey) ||
                                 itemKey.Equals(DefaultFishingSource.radioactiveCarpKey))
                        {
                            var newInfo = entry.AvailabilityInfo with
                            {
                                When = DefaultFishingSource.isLegendaryFamilyActive
                            };
                            entry = entry with { AvailabilityInfo = newInfo };
                        }

                        fishEntries.Add(entry);
                    }
                }
            }

            return new(this.manifest) { AddFish = fishEntries.ToImmutableArray() };
        }

        private void ApplyLegendaryLogic(ref FishEntry entry)
        {
            var conditions = new Dictionary<string, string?>(DefaultFishingSource.legendaryBaseConditions);
            var newInfo = entry.AvailabilityInfo with
            {
                When = conditions.ToImmutableDictionary()
            };
            entry = entry with { AvailabilityInfo = newInfo };
        }

        private bool TryParseFish(
            string[] fishInfo,
            [NotNullWhen(true)] out ImmutableArray<FishAvailabilityInfo>? availabilities
        )
        {
            availabilities = default;

            var weathers = Weathers.None;
            var weatherStrings = fishInfo[7].Split(' ');
            foreach (var weatherString in weatherStrings)
            {
                weathers |= weatherString switch
                {
                    "sunny" => Weathers.Sunny,
                    "rainy" => Weathers.Rainy,
                    "both" => Weathers.All,
                    _ => Weathers.None,
                };
            }

            if (!float.TryParse(fishInfo[10], out var weightedChance))
            {
                return false;
            }

            if (!int.TryParse(fishInfo[12], out var minFishingLevel))
            {
                return false;
            }

            availabilities = GenerateAvailabilities().ToImmutableArray();
            return true;

            IEnumerable<FishAvailabilityInfo> GenerateAvailabilities()
            {
                var times = fishInfo[5].Split(' ');
                for (var i = 0; i < times.Length - 1; i += 2)
                {
                    if (!int.TryParse(times[i], out var startTime))
                    {
                        continue;
                    }

                    if (!int.TryParse(times[i + 1], out var endTime))
                    {
                        continue;
                    }

                    yield return new FishAvailabilityInfo(weightedChance)
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        Weathers = weathers,
                        MinFishingLevel = minFishingLevel,
                    };
                }
            }
        }
    }
}
