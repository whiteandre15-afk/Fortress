using System;

namespace Fortress.Building
{
    /// Sizes of the building grid, in metres.
    public static class BuildGrid
    {
        /// Width of one building square.
        public const float Unit = 3;
        public const float WallHeight = 3;
        /// Thickness of floors.
        public const float Slab = 0.25f;
        public const float WallThickness = 0.2f;
        /// Height of one storey: its floor slab plus its walls.
        public const float Storey = Slab + WallHeight;
        public const float DoorWidth = 1;
        /// Aiming this far below the aim point picks the floor line, so looking straight ahead from
        /// eye height floors at your feet and you look up to put in a ceiling.
        public const float FloorAimBias = 0.5f;
    }

    public enum PieceKind { Wall, Door, Window, Floor, Ramp }

    /// What a piece is built from. Each one is mined from something in the world.
    public enum BuildMaterial { Wood, Brick, Metal }

    public readonly struct MaterialStats
    {
        /// Health of a finished piece.
        public readonly float MaxHP;
        /// What one piece costs.
        public readonly int Cost;
        /// Seconds to reach full health once placed (pieces start at 30%).
        public readonly float BuildSeconds;
        /// How much one hit on its source (tree, rock, wreck) yields.
        public readonly int PerHit;

        public MaterialStats(float maxHP, int cost, float buildSeconds, int perHit)
        {
            MaxHP = maxHP;
            Cost = cost;
            BuildSeconds = buildSeconds;
            PerHit = perHit;
        }
    }

    public static class PieceInfo
    {
        /// Walls, doors and windows all stand on the side of a square.
        public static bool StandsOnEdge(this PieceKind kind) =>
            kind == PieceKind.Wall || kind == PieceKind.Door || kind == PieceKind.Window;

        public static MaterialStats Stats(this BuildMaterial material) => material switch
        {
            BuildMaterial.Wood => new MaterialStats(150, 10, 0, 10),
            BuildMaterial.Brick => new MaterialStats(300, 10, 1, 8),
            _ => new MaterialStats(450, 10, 2, 6),
        };
    }

    /// Which part of a square a slot is. Each square owns its low-z side (EdgeX, running along x
    /// from corner (x, z) to (x + 1, z)) and its low-x side (EdgeZ, running along z from (x, z) to (x, z + 1)).
    public enum Layer { Floor, Ramp, EdgeX, EdgeZ }

    /// A grid corner, where the ends of walls meet.
    public readonly struct Corner : IEquatable<Corner>
    {
        public readonly int X, Z, Level;

        public Corner(int x, int z, int level)
        {
            X = x;
            Z = z;
            Level = level;
        }

        public bool Equals(Corner other) => X == other.X && Z == other.Z && Level == other.Level;
        public override bool Equals(object obj) => obj is Corner other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Z, Level);
    }

    /// A place on the building grid where exactly one piece can sit.
    public readonly struct Slot : IEquatable<Slot>
    {
        public readonly int X, Z;
        /// Storey, counting up from 0 at ground level.
        public readonly int Level;
        public readonly Layer Layer;

        public Slot(int x, int z, int level, Layer layer)
        {
            X = x;
            Z = z;
            Level = level;
            Layer = layer;
        }

        public bool IsEdge => Layer == Layer.EdgeX || Layer == Layer.EdgeZ;

        public Slot Moved(int dx = 0, int dz = 0, int levels = 0, Layer? layer = null) =>
            new Slot(X + dx, Z + dz, Level + levels, layer ?? Layer);

        /// The squares this slot belongs to: the two either side of an edge, or its own square.
        public Slot[] Cells => Layer switch
        {
            Layer.EdgeX => new[] { Moved(dz: -1, layer: Layer.Floor), Moved(layer: Layer.Floor) },
            Layer.EdgeZ => new[] { Moved(dx: -1, layer: Layer.Floor), Moved(layer: Layer.Floor) },
            _ => new[] { Moved(layer: Layer.Floor) },
        };

        /// The four edges around this slot's square, on the same storey.
        public Slot[] Sides => new[]
        {
            Moved(layer: Layer.EdgeX), Moved(dz: 1, layer: Layer.EdgeX),
            Moved(layer: Layer.EdgeZ), Moved(dx: 1, layer: Layer.EdgeZ),
        };

        /// The four squares next to this slot's square, as floor slots.
        public Slot[] AdjacentCells => new[]
        {
            Moved(dx: 1, layer: Layer.Floor), Moved(dx: -1, layer: Layer.Floor),
            Moved(dz: 1, layer: Layer.Floor), Moved(dz: -1, layer: Layer.Floor),
        };

        /// The two grid corners at the ends of an edge.
        public Corner[] Ends => Layer switch
        {
            Layer.EdgeX => new[] { new Corner(X, Z, Level), new Corner(X + 1, Z, Level) },
            Layer.EdgeZ => new[] { new Corner(X, Z, Level), new Corner(X, Z + 1, Level) },
            _ => Array.Empty<Corner>(),
        };

        /// The slot a piece of `kind` snaps to around aim point (x, y, z), for a player facing
        /// quarter turn `heading` (0 is toward -z, then -x, +z, +x), keeping to storeys minLevel...maxLevel.
        public static Slot Snapped(PieceKind kind, float x, float y, float z, int heading, int minLevel, int maxLevel)
        {
            int cx = (int)Math.Floor(x / BuildGrid.Unit), cz = (int)Math.Floor(z / BuildGrid.Unit);
            int Clamped(int level) => Math.Min(Math.Max(level, minLevel), maxLevel);
            int inStorey = Clamped((int)Math.Floor(y / BuildGrid.Storey));
            switch (kind)
            {
                case PieceKind.Floor:
                    return new Slot(cx, cz, Clamped((int)Math.Round((y - BuildGrid.FloorAimBias) / BuildGrid.Storey, MidpointRounding.AwayFromZero)), Layer.Floor);
                case PieceKind.Ramp:
                    return new Slot(cx, cz, inStorey, Layer.Ramp);
                default:
                    // A wall always faces the player: of the two sides across their view, take the nearer to the aim point.
                    var square = new Slot(cx, cz, inStorey, Layer.Floor);
                    if (heading % 2 == 0)
                    {
                        float fz = z - cz * BuildGrid.Unit;
                        return square.Moved(dz: fz < BuildGrid.Unit / 2 ? 0 : 1, layer: Layer.EdgeX);
                    }
                    float fx = x - cx * BuildGrid.Unit;
                    return square.Moved(dx: fx < BuildGrid.Unit / 2 ? 0 : 1, layer: Layer.EdgeZ);
            }
        }

        public bool Equals(Slot other) => X == other.X && Z == other.Z && Level == other.Level && Layer == other.Layer;
        public override bool Equals(object obj) => obj is Slot other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Z, Level, (int)Layer);
        public static bool operator ==(Slot a, Slot b) => a.Equals(b);
        public static bool operator !=(Slot a, Slot b) => !a.Equals(b);
        public override string ToString() => $"{Layer}({X}, {Z}, L{Level})";
    }
}
