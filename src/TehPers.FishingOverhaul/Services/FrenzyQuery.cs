using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;
using StardewValley.Delegates;

namespace TehPers.FishingOverhaul.Services
{
    /// <summary>
    /// Registers custom GameStateQueries for Fishing Overhaul.
    /// </summary>
    public static class FrenzyQuery
    {
        private static bool isRegistered = false;

        /// <summary>
        /// Registers the custom queries with Stardew Valley's engine.
        /// </summary>
        public static void Register()
        {
            if (isRegistered)
            {
                return;
            }

            // 1. Frénésie (Bulles)
            GameStateQuery.Register("CATCHING_FRENZY_FISH", CheckFrenzy);

            // 2. Règles Qi (Legendary Family)
            GameStateQuery.Register("PLAYER_HAS_SPECIAL_ORDER_RULE", CheckSpecialOrderRule);

            // 3. Position du Bouchon (Pour le poisson Legend/Crimsonfish)
            // Syntaxe : BOBBER_IN_RECT <X> <Y> <W> <H>
            GameStateQuery.Register("BOBBER_IN_RECT", CheckBobberInRect);

            // 4. Profondeur de l'eau (Pour le poisson Legend)
            // Syntaxe : WATER_DEPTH <MinDepth>
            GameStateQuery.Register("WATER_DEPTH", CheckWaterDepth);

            // 5. Position Joueur (Manquant dans Vanilla pour ce contexte spécifique)
            GameStateQuery.Register("PLAYER_TILE_X", CheckPlayerTileX);
            GameStateQuery.Register("PLAYER_TILE_Y", CheckPlayerTileY);

            isRegistered = true;
        }

        private static bool CheckWaterDepth(string[] query, GameStateQueryContext context)
        {
            if (query.Length < 2)
            {
                return false;
            }
            if (!int.TryParse(query[1], out var minDepth))
            {
                return false;
            }

            if (context.Player.CurrentTool is FishingRod rod)
            {
                // clearWaterDistance est la valeur utilisée par le jeu pour la "zone" de pêche (0 à 5)
                return rod.clearWaterDistance >= minDepth;
            }
            return false;
        }

        private static bool CheckSpecialOrderRule(string[] query, GameStateQueryContext context)
        {
            if (query.Length < 3)
            {
                return false;
            }

            var ruleName = query[2];
            return Game1.player.team.SpecialOrderRuleActive(ruleName);
        }

        private static bool CheckPlayerTileX(string[] query, GameStateQueryContext context)
        {
            if (query.Length < 4)
            {
                return false;
            }
            if (context.Player is not { } player)
            {
                return false;
            }
            if (!int.TryParse(query[2], out var min) || !int.TryParse(query[3], out var max))
            {
                return false;
            }
            return player.TilePoint.X >= min && player.TilePoint.X < max;
        }

        private static bool CheckPlayerTileY(string[] query, GameStateQueryContext context)
        {
            if (query.Length < 4)
            {
                return false;
            }
            if (context.Player is not { } player)
            {
                return false;
            }
            if (!int.TryParse(query[2], out var min) || !int.TryParse(query[3], out var max))
            {
                return false;
            }
            return player.TilePoint.Y >= min && player.TilePoint.Y < max;
        }

        private static bool CheckBobberInRect(string[] query, GameStateQueryContext context)
        {
            if (query.Length < 5)
            {
                return false;
            }

            if (!int.TryParse(query[1], out var x) || !int.TryParse(query[2], out var y) ||
                !int.TryParse(query[3], out var w) || !int.TryParse(query[4], out var h))
            {
                return false;
            }

            if (context.Player.CurrentTool is FishingRod rod)
            {
                var bobberX = (int)(rod.bobber.X / 64f);
                var bobberY = (int)(rod.bobber.Y / 64f);

                return bobberX >= x && bobberX < x + w && bobberY >= y && bobberY < y + h;
            }
            return false;
        }

        private static bool CheckFrenzy(string[] query, GameStateQueryContext context)
        {
            if (context.Location is not { } location)
            {
                return false;
            }
            if (context.Player is not { } player)
            {
                return false;
            }
            if (query.Length < 2)
            {
                return false;
            }

            var targetFishId = query[1];
            if (location.fishFrenzyFish.Value != targetFishId)
            {
                return false;
            }

            if (player.CurrentTool is FishingRod rod)
            {
                var splashPoint = location.fishSplashPoint.Value;
                if (splashPoint == Point.Zero)
                {
                    return false;
                }

                var bobberTileX = (int)(rod.bobber.X / 64f);
                var bobberTileY = (int)(rod.bobber.Y / 64f);

                return bobberTileX == splashPoint.X && bobberTileY == splashPoint.Y;
            }

            return false;
        }
    }
}
