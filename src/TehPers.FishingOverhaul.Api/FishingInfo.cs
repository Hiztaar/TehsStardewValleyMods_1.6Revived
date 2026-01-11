using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Tools;
using TehPers.Core.Api.Gameplay;
using TehPers.Core.Api.Items;

namespace TehPers.FishingOverhaul.Api
{
    /// <summary>
    /// Information about a <see cref="Farmer"/> that is fishing.
    /// </summary>
    public record FishingInfo(Farmer User)
    {
        private static readonly Func<FishingRod, int> getBobberDepth;

        static FishingInfo()
        {
            var rod = Expression.Parameter(typeof(FishingRod), "rod");
            var bobberDepthField = Expression.Field(rod, "clearWaterDistance");
            var getBobberDepthExpr =
                Expression.Lambda<Func<FishingRod, int>>(bobberDepthField, rod);
            FishingInfo.getBobberDepth = getBobberDepthExpr.Compile();
        }

        /// <summary>
        /// The times of day being fished at.
        /// </summary>
        public ImmutableArray<int> Times { get; init; } = ImmutableArray.Create(Game1.timeOfDay);

        /// <summary>
        /// The seasons being fished in.
        /// </summary>
        public Seasons Seasons { get; init; } = User.currentLocation.GetSeason() switch
        {
            Season.Spring => Seasons.Spring,
            Season.Summer => Seasons.Summer,
            Season.Fall => Seasons.Fall,
            Season.Winter => Seasons.Winter,
            _ => Seasons.All,
        };

        /// <summary>
        /// The weathers being fished in.
        /// </summary>
        public Weathers Weathers { get; init; } = Game1.isRaining switch
        {
            true => Weathers.Rainy,
            false => Weathers.Sunny,
        };

        /// <summary>
        /// The water types being fished in.
        /// </summary>
        public WaterTypes WaterTypes { get; init; } = GetWaterType(User);

        /// <summary>
        /// (Optional) Flag to set when fish is caught.
        /// </summary>
        public string? SetFlagOnCatch { get; init; } = null;

        /// <summary>
        /// The fishing level of the <see cref="Farmer"/> that is fishing.
        /// </summary>
        public int FishingLevel { get; init; } = User.FishingLevel;

        /// <summary>
        /// The bobber depth.
        /// </summary>
        public int BobberDepth { get; init; } =
            User.CurrentTool is FishingRod { isFishing: true } rod
                ? FishingInfo.getBobberDepth(rod)
                : 4;

        /// <summary>
        /// The names of the locations being fished in.
        /// </summary>
        public ImmutableArray<string> Locations { get; init; } = FishingInfo
            .GetDefaultLocationNames(User.currentLocation, User.Tile)
            .ToImmutableArray();

        /// <summary>
        /// The fishing rod's bobber position.
        /// </summary>
        public Vector2 BobberPosition { get; init; } =
            User.CurrentTool is FishingRod { isFishing: true, bobber: { Value: var bobberPos } }
                ? bobberPos / 64
                : User.getStandingPosition() / 64;

        /// <summary>
        /// The bait used for fishing.
        /// </summary>
        public NamespacedKey? Bait { get; } = User.CurrentTool is FishingRod rod && rod.GetBait() is { } bait
            ? NamespacedKey.SdvObject(bait.ItemId)
            : null;

        /// <summary>
        /// The fish targeted by the bait, if any.
        /// Updated for Stardew Valley 1.6 logic.
        /// </summary>
        public NamespacedKey? TargetedFish { get; } = GetTargetedFish(User.CurrentTool as FishingRod);

        /// <summary>
        /// The bobber/tackle used for fishing.
        /// </summary>
        public NamespacedKey? Bobber { get; } = User.CurrentTool is FishingRod rod
            ? FishingInfo.ConvertAttachmentIndex(rod.attachments.IndexOf(rod.GetTackle().FirstOrDefault()))
            : null;

        private static NamespacedKey? GetTargetedFish(FishingRod? rod)
        {
            if (rod?.GetBait() is not StardewValley.Object bait)
            {
                return null;
            }

            // 1. Detect if bait is specialized
            // FIX: Stardew Valley 1.6 uses "SpecificBait" as the Item ID for Targeted Bait.
            var isTargetedBait = bait.ItemId == "SpecificBait"
                                 || bait.HasContextTag("targeted_bait")
                                 || bait.Name.Contains("Specific Bait");

            if (!isTargetedBait)
            {
                return null;
            }

            // 2. Extract the Preserved Item ID (Target Fish)
            // In 1.6, this is stored in preservedParentSheetIndex (NetString)
            var preservedId = bait.preservedParentSheetIndex.Value;

            if (string.IsNullOrWhiteSpace(preservedId))
            {
                return null;
            }

            // 3. Resolve the ID to ensure it matches the game's item data
            if (ItemRegistry.GetData(preservedId) is { } data)
            {
                return NamespacedKey.SdvObject(data.ItemId);
            }

            return null;
        }

        private static WaterTypes GetWaterType(Farmer farmer)
        {
            if (farmer.currentLocation.TryGetFishAreaForTile(farmer.Tile, out var id, out _))
            {
                return id switch
                {
                    "0" => WaterTypes.River,
                    "1" => WaterTypes.PondOrOcean,
                    "2" => WaterTypes.Freshwater,
                    _ => WaterTypes.All,
                };
            }
            return WaterTypes.All;
        }

        private static NamespacedKey? ConvertAttachmentIndex(int index)
        {
            return index switch
            {
                < 0 => null,
                _ => NamespacedKey.SdvObject(index),
            };
        }

        /// <summary>
        /// Gets the location names associated with a <see cref="GameLocation"/>. Some locations
        /// have names in addition to their normal names:
        ///
        /// <list type="bullet">
        ///     <item>
        ///         <term><see cref="MineShaft"/></term>
        ///         <description>"UndergroundMine/#", where # is the floor number.</description>
        ///     </item>
        ///     <item>
        ///         <term><see cref="Farm"/></term>
        ///         <description>
        ///             "Farm/X", where X is one of "Standard", "Riverland", "Forest", "Hills",
        ///             "Wilderness", or "FourCorners". Only vanilla farms have this name.
        ///         </description>
        ///     </item>
        ///     <item>
        ///         <term><see cref="IslandLocation"/></term>
        ///         <description>"Island".</description>
        ///     </item>
        /// </list>
        /// </summary>
        /// <param name="location">The location to get the names of.</param>
        /// <param name="tile">The tile position (optional) for context-sensitive overrides.</param>
        /// <returns>The location's names.</returns>
        public static IEnumerable<string> GetDefaultLocationNames(GameLocation location, Vector2 tile = default)
        {
            var names = new List<string>();

            // 1. Standard Names
            switch (location)
            {
                case MineShaft { Name: { } name, mineLevel: var mineLevel }:
                    names.Add(name);
                    names.Add("UndergroundMine");
                    names.Add($"UndergroundMine/{mineLevel}");
                    break;

                case Farm { Name: { } name }:
                    names.Add(name);
                    var suffix = Game1.whichFarm switch
                    {
                        0 => "Standard",
                        1 => "Riverland",
                        2 => "Forest",
                        3 => "Hills",
                        4 => "Wilderness",
                        5 => "FourCorners",
                        6 => "Beach",
                        7 => "Meadows",
                        _ => null
                    };
                    if (suffix != null)
                    {
                        names.Add($"{name}/{suffix}");
                    }
                    break;

                case IslandLocation { Name: { } name }:
                    names.Add(name);
                    names.Add("Island");
                    break;

                default:
                    names.Add(location.Name);
                    break;
            }

            // 2. Stardew Valley 1.6 API Override
            // This captures overrides from SVE and other mods that define fishing locations dynamically
            if (location.GetFishingLocation(tile) is string overrideName && !string.IsNullOrWhiteSpace(overrideName))
            {
                names.Add(overrideName);
            }

            return names.Distinct();
        }
    }
}
