using System.Collections.Generic;
using Fortress.Building;

namespace Fortress.Game
{
    /// The materials the player is carrying.
    public static class Stock
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// Development builds: gun from the start and building costs nothing. Mining still counts up.
        public const bool Unlimited = true;
#else
        public const bool Unlimited = false;
#endif

        static readonly Dictionary<BuildMaterial, int> counts = new();

        public static int Count(BuildMaterial material) => counts.TryGetValue(material, out var count) ? count : 0;

        public static bool CanAfford(BuildMaterial material) => Unlimited || Count(material) >= material.Stats().Cost;

        public static void Add(BuildMaterial material, int amount) => counts[material] = Count(material) + amount;

        /// Takes `amount` if there's enough. Returns whether it was taken.
        public static bool Spend(BuildMaterial material, int amount)
        {
            if (Unlimited) return true;
            if (Count(material) < amount) return false;
            counts[material] = Count(material) - amount;
            return true;
        }

        public static void Reset() => counts.Clear();
    }
}
