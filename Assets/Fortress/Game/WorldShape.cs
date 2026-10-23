using UnityEngine;

namespace Fortress.Game
{
    /// The shape of the land, shared by the editor (which bakes the terrain from it) and the game
    /// (which places trees and rocks by it). Everything is fixed noise, so the world is the same
    /// every time.
    public static class WorldShape
    {
        /// Half the width of the land, in metres. The terrain runs from -Extent to +Extent.
        public const float Extent = 800;
        /// The tallest the terrain can be.
        public const float MaxHeight = 60;

        static float Noise(float x, float z, float scale, float offset) => Mathf.PerlinNoise(x / scale + offset, z / scale + offset * 1.7f);

        /// Rolling hills: broad swells with smaller bumps on them. Never below 0.
        public static float Height(float x, float z)
        {
            float broad = Noise(x, z, 340, 13.1f), middle = Noise(x, z, 110, 47.3f), small = Noise(x, z, 28, 91.7f);
            // Squaring the broad swell flattens the valleys and steepens the tops, like real hills.
            return 34 * broad * broad + 9 * middle + 1.6f * small;
        }

        /// How wooded a spot is, 0 (open meadow) to 1 (thick forest).
        public static float Forest(float x, float z) => Mathf.SmoothStep(0, 1, (Noise(x, z, 160, 5.5f) - 0.38f) / 0.3f);

        /// Where bare mud shows through, 0 to 1.
        public static float Mud(float x, float z) => Mathf.SmoothStep(0, 1, (Noise(x, z, 45, 71.2f) - 0.66f) / 0.12f);
    }
}
