using System.Collections.Generic;
using System.Collections.Immutable;
using StardewValley;
using TehPers.Core.Api.Items;
using TehPers.FishingOverhaul.Api.Content;
using TehPers.FishingOverhaul.Content;

namespace TehPers.FishingOverhaul.Services
{
    internal sealed partial class DefaultFishingSource
    {
        private FishingContent GetDefaultTrashData()
        {
            return new(this.manifest) { AddTrash = GenerateTrashData().ToImmutableArray() };

            // Correction Avertissement IDE0062: Fonction locale rendue statique
            static IEnumerable<TrashEntry> GenerateTrashData()
            {
                // Joja cola
                yield return new(
                    NamespacedKey.SdvObject(167),
                    new(1.0D) { ExcludeLocations = ImmutableArray.Create("Submarine") }
                );
                // Trash
                yield return new(
                    NamespacedKey.SdvObject(168),
                    new(1.0D) { ExcludeLocations = ImmutableArray.Create("Submarine") }
                );
                // Driftwood
                yield return new(
                    NamespacedKey.SdvObject(169),
                    new(1.0D) { ExcludeLocations = ImmutableArray.Create("Submarine") }
                );
                // Broken Glasses
                yield return new(
                    NamespacedKey.SdvObject(170),
                    new(1.0D) { ExcludeLocations = ImmutableArray.Create("Submarine") }
                );
                // Broken CD
                yield return new(
                    NamespacedKey.SdvObject(171),
                    new(1.0D) { ExcludeLocations = ImmutableArray.Create("Submarine") }
                );
                // Soggy Newspaper
                yield return new(
                    NamespacedKey.SdvObject(172),
                    new(1.0D) { ExcludeLocations = ImmutableArray.Create("Submarine") }
                );

                // RESTORED 1.6 ITEMS (Seaweed, Algae)
                // Seaweed
                yield return new(
                    NamespacedKey.SdvObject(152),
                    new(0.5D) { IncludeLocations = ImmutableArray.Create("Beach", "IslandWest", "IslandSouth", "IslandSouthEast", "Submarine") }
                );
                // Green Algae
                yield return new(
                    NamespacedKey.SdvObject(153),
                    new(0.5D) { ExcludeLocations = ImmutableArray.Create("Beach", "IslandWest", "IslandSouth", "IslandSouthEast", "Submarine") }
                );
                // White Algae
                yield return new(
                    NamespacedKey.SdvObject(157),
                    new(1.0D) { IncludeLocations = ImmutableArray.Create("UndergroundMine", "Sewer", "BugLand", "WitchSwamp", "MutantBugLair") }
                );

                // Wall Basket (https://stardewvalleywiki.com/Secrets#Basket)
                yield return new(
                    NamespacedKey.SdvObject(79),
                    new(0.5)
                    {
                        IncludeLocations = ImmutableArray.Create("BusStop"),
                        When = new Dictionary<string, string?>
                        {
                            ["HasFlag |contains=linusBasket"] = "false",
                            ["HasReadLetter |contains=LinusBasket"] = "true",
                        }.ToImmutableDictionary(),
                    }
                )
                { OnCatch = new() { SetFlags = ImmutableArray.Create("linusBasket") } };

                // Golden Walnut (https://stardewvalleywiki.com/Golden_Walnut)
                yield return new(
                    NamespacedKey.SdvObject(73),
                    new(0.5)
                    {
                        IncludeLocations = ImmutableArray.Create("IslandNorth"),
                        FarmerPosition = new()
                        {
                            X = new() { LessThan = 6 },
                            Y = new() { GreaterThan = 48 },
                        },
                        When = new Dictionary<string, string?>
                        {
                            ["TehPers.FishingOverhaul/GoldenWalnutCount |ExampleMod.Token"] = "0"
                        }.ToImmutableDictionary(),
                    }
                );

                // Vista Painting (https://stardewvalleywiki.com/Secrets#Vista_Painting)
                yield return new(
                    NamespacedKey.SdvObject(Game1.year == 1 ? "MyFirstPainting" : "1200"),
                    new(0.5)
                    {
                        IncludeLocations = ImmutableArray.Create("Forest"),
                        FarmerPosition = new()
                        {
                            X = new()
                            {
                                GreaterThan = 43,
                                LessThan = 53,
                            },
                            Y = new()
                            {
                                GreaterThanEq = 98,
                                LessThan = 99,
                            },
                        },
                        When = new Dictionary<string, string?>
                        {
                            ["HasFlag |contains=gotBoatPainting"] = "false"
                        }.ToImmutableDictionary(),
                    }
                )
                { OnCatch = new() { SetFlags = ImmutableArray.Create("gotBoatPainting") } };

                // Dove children (https://stardewvalleywiki.com/Secrets#Dove_Children)
                yield return new(
                    NamespacedKey.SdvObject(103),
                    new(0.5)
                    {
                        IncludeLocations = ImmutableArray.Create("Farm/FourCorners"),
                        FarmerPosition = new()
                        {
                            X = new() { LessThan = 40 },
                            Y = new() { GreaterThan = 54 },
                        },
                        When = new Dictionary<string, string?>
                        {
                            ["HasFlag"] = "cursed_doll",
                            ["HasFlag |contains=eric's_prank_1"] = "false",
                        }.ToImmutableDictionary(),
                    }
                )
                { OnCatch = new() { SetFlags = ImmutableArray.Create("eric's_prank_1") } };
            }
        }
    }
}
