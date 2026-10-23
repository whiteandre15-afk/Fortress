using System.Collections.Generic;
using System.Linq;

namespace Fortress.Building
{
    /// What the land under the grid is like: which pieces it holds up, and which it swallows.
    public interface IGround
    {
        /// Whether the earth itself holds this piece up.
        bool Supports(Slot slot);
        /// Whether the piece would sit entirely underground.
        bool Buries(Slot slot);
    }

    /// Level ground at height 0: storey-0 pieces stand on it, and nothing goes below it.
    public sealed class FlatGround : IGround
    {
        public static readonly FlatGround Instance = new();
        public bool Supports(Slot slot) => slot.Level == 0;
        public bool Buries(Slot slot) => slot.Level < 0;
    }

    /// The building rules. `pieces` is everything already built, keyed by where it sits.
    public static class BuildRules
    {
        /// The slots touching `slot`. A piece holds up, and is held up by, every piece it touches,
        /// and if A lists B then B lists A.
        public static List<Slot> Neighbors(Slot slot)
        {
            var touching = new List<Slot>();
            switch (slot.Layer)
            {
                case Layer.Floor:
                    // Floors beside it, walls standing on it and holding it up, ramps on it and ramps arriving at it.
                    touching.AddRange(slot.AdjacentCells);
                    foreach (var side in slot.Sides) touching.AddRange(new[] { side, side.Moved(levels: -1) });
                    touching.Add(slot.Moved(layer: Layer.Ramp));
                    touching.Add(slot.Moved(levels: -1, layer: Layer.Ramp));
                    foreach (var cell in slot.AdjacentCells)
                    {
                        touching.Add(cell.Moved(layer: Layer.Ramp));
                        touching.Add(cell.Moved(levels: -1, layer: Layer.Ramp));
                    }
                    break;
                case Layer.Ramp:
                    // Floors at its foot and its top, its square's walls, and ramps and floors next to it.
                    touching.Add(slot.Moved(layer: Layer.Floor));
                    touching.Add(slot.Moved(levels: 1, layer: Layer.Floor));
                    touching.AddRange(slot.Sides);
                    foreach (var cell in slot.AdjacentCells)
                    {
                        touching.Add(cell);
                        touching.Add(cell.Moved(levels: 1));
                        for (int levels = -1; levels <= 1; levels++) touching.Add(cell.Moved(levels: levels, layer: Layer.Ramp));
                    }
                    break;
                default:
                    // The floors it stands on and holds up, the walls above, below and meeting its ends, and the ramps beside it.
                    foreach (var cell in slot.Cells)
                    {
                        touching.Add(cell);
                        touching.Add(cell.Moved(levels: 1));
                        touching.Add(cell.Moved(layer: Layer.Ramp));
                    }
                    touching.Add(slot.Moved(levels: 1));
                    touching.Add(slot.Moved(levels: -1));
                    foreach (var corner in slot.Ends) touching.AddRange(EdgesMeetingAt(corner).Where(edge => edge != slot));
                    break;
            }
            return touching;
        }

        /// Every edge slot with an end at `corner`.
        public static Slot[] EdgesMeetingAt(Corner corner) => new[]
        {
            new Slot(corner.X, corner.Z, corner.Level, Layer.EdgeX),
            new Slot(corner.X - 1, corner.Z, corner.Level, Layer.EdgeX),
            new Slot(corner.X, corner.Z, corner.Level, Layer.EdgeZ),
            new Slot(corner.X, corner.Z - 1, corner.Level, Layer.EdgeZ),
        };

        /// A piece goes in an empty slot that is held up by the ground or touching something already
        /// built, and not swallowed by the ground. The ghost preview turns red wherever this returns false.
        public static bool CanPlace(PieceKind kind, Slot slot, IReadOnlyDictionary<Slot, PieceKind> pieces, IGround ground = null)
        {
            ground ??= FlatGround.Instance;
            if (slot.Level < 0 || pieces.ContainsKey(slot) || ground.Buries(slot)) return false;
            return ground.Supports(slot) || Neighbors(slot).Any(pieces.ContainsKey);
        }

        /// The pieces no longer joined to the ground through a chain of touching pieces. They fall.
        public static HashSet<Slot> Unsupported(IReadOnlyDictionary<Slot, PieceKind> pieces, IGround ground = null)
        {
            ground ??= FlatGround.Instance;
            var held = new HashSet<Slot>(pieces.Keys.Where(ground.Supports));
            var frontier = new Stack<Slot>(held);
            while (frontier.Count > 0)
            {
                foreach (var next in Neighbors(frontier.Pop()))
                {
                    if (pieces.ContainsKey(next) && held.Add(next)) frontier.Push(next);
                }
            }
            var unheld = new HashSet<Slot>(pieces.Keys);
            unheld.ExceptWith(held);
            return unheld;
        }
    }
}
