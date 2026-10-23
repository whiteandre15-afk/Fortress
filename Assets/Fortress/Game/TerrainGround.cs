using Fortress.Building;
using UnityEngine;

namespace Fortress.Game
{
    /// The building rules' view of the hills: which pieces the terrain holds up, and which it buries.
    public class TerrainGround : IGround
    {
        /// How far above the ground a wall or ramp's base may sit and still count as standing on it.
        const float Tolerance = 0.3f;

        readonly Terrain terrain;

        public TerrainGround(Terrain terrain) => this.terrain = terrain;

        public float HeightAt(float x, float z) => terrain ? terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y : 0;

        /// The lowest and highest ground under a slot's footprint: the corners and middle of its square,
        /// or the ends and middle of its edge.
        public (float low, float high) Under(Slot slot)
        {
            float x = slot.X * BuildGrid.Unit, z = slot.Z * BuildGrid.Unit, u = BuildGrid.Unit;
            Vector2[] points = slot.Layer switch
            {
                Layer.EdgeX => new[] { new Vector2(x, z), new Vector2(x + u / 2, z), new Vector2(x + u, z) },
                Layer.EdgeZ => new[] { new Vector2(x, z), new Vector2(x, z + u / 2), new Vector2(x, z + u) },
                _ => new[] { new Vector2(x, z), new Vector2(x + u, z), new Vector2(x, z + u), new Vector2(x + u, z + u), new Vector2(x + u / 2, z + u / 2) },
            };
            float low = float.MaxValue, high = float.MinValue;
            foreach (var point in points)
            {
                float h = HeightAt(point.x, point.y);
                low = Mathf.Min(low, h);
                high = Mathf.Max(high, h);
            }
            return (low, high);
        }

        public bool Supports(Slot slot)
        {
            float gap = slot.Level * BuildGrid.Storey - Under(slot).high;
            // Floors stand on stilts across up to a storey; walls and ramps need the ground at their base.
            return slot.Layer == Layer.Floor ? gap <= BuildGrid.Storey : gap <= Tolerance;
        }

        public bool Buries(Slot slot)
        {
            float top = slot.Level * BuildGrid.Storey + (slot.Layer == Layer.Floor ? BuildGrid.Slab : BuildGrid.Storey);
            return Under(slot).low >= top;
        }

        /// How far a floor's underside sits above the ground at each of its corners, for stilts.
        public float GapUnder(float x, float z, int level) => level * BuildGrid.Storey - HeightAt(x, z);
    }
}
