using System.Collections.Generic;
using Fortress.Building;
using NUnit.Framework;

namespace Fortress.Tests
{
    public class BuildRulesTests
    {
        static readonly Layer[] Layers = { Layer.Floor, Layer.Ramp, Layer.EdgeX, Layer.EdgeZ };

        /// If A holds up B, B holds up A; otherwise collapse would depend on which way the search ran.
        [Test]
        public void NeighborsAreSymmetric()
        {
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            for (int level = 0; level <= 2; level++)
            foreach (var layer in Layers)
            {
                var slot = new Slot(x, z, level, layer);
                foreach (var other in BuildRules.Neighbors(slot))
                {
                    Assert.IsTrue(BuildRules.Neighbors(other).Contains(slot), $"{other} does not list {slot}");
                    Assert.AreNotEqual(slot, other);
                }
            }
        }

        [Test]
        public void RampsChainFromTheGround()
        {
            var pieces = new Dictionary<Slot, PieceKind>();
            for (int step = 0; step < 4; step++)
            {
                var slot = new Slot(step, 0, step, Layer.Ramp);
                Assert.IsTrue(BuildRules.CanPlace(PieceKind.Ramp, slot, pieces), $"ramp {step}");
                pieces[slot] = PieceKind.Ramp;
            }
            Assert.IsEmpty(BuildRules.Unsupported(pieces));
        }

        [Test]
        public void FloorRunsOutOffAWall()
        {
            var pieces = new Dictionary<Slot, PieceKind> { [new Slot(0, 0, 0, Layer.EdgeX)] = PieceKind.Wall };
            // The first floor rests on top of the wall; the next three hang off it with nothing below.
            for (int x = 0; x < 4; x++)
            {
                var slot = new Slot(x, 0, 1, Layer.Floor);
                Assert.IsTrue(BuildRules.CanPlace(PieceKind.Floor, slot, pieces), $"floor {x}");
                pieces[slot] = PieceKind.Floor;
            }
        }

        [Test]
        public void WallGoesOnTopOfAWall()
        {
            var below = new Slot(0, 0, 0, Layer.EdgeZ);
            var pieces = new Dictionary<Slot, PieceKind> { [below] = PieceKind.Wall };
            Assert.IsTrue(BuildRules.CanPlace(PieceKind.Wall, below.Moved(levels: 1), pieces));
        }

        [Test]
        public void FloatingPieceIsRejected()
        {
            var pieces = new Dictionary<Slot, PieceKind> { [new Slot(0, 0, 0, Layer.Floor)] = PieceKind.Floor };
            Assert.IsFalse(BuildRules.CanPlace(PieceKind.Floor, new Slot(0, 0, 2, Layer.Floor), pieces));
            Assert.IsFalse(BuildRules.CanPlace(PieceKind.Floor, new Slot(0, 0, 0, Layer.Floor), pieces), "occupied");
        }

        [Test]
        public void CuttingTheBaseDropsWhatItHeld()
        {
            // A two-storey wall tower with a floor on top, plus a floor beside the base on the ground.
            var bottom = new Slot(0, 0, 0, Layer.EdgeX);
            var upper = bottom.Moved(levels: 1);
            var roof = new Slot(0, 0, 2, Layer.Floor);
            var ground = new Slot(0, 0, 0, Layer.Floor);
            var pieces = new Dictionary<Slot, PieceKind>
            {
                [bottom] = PieceKind.Wall, [upper] = PieceKind.Wall, [roof] = PieceKind.Floor, [ground] = PieceKind.Floor,
            };
            Assert.IsEmpty(BuildRules.Unsupported(pieces));

            pieces.Remove(bottom);
            CollectionAssert.AreEquivalent(new[] { upper, roof }, BuildRules.Unsupported(pieces));
        }

        [Test]
        public void SideBranchTouchingGroundSurvives()
        {
            // Two wall columns joined by a floor on top; cut one column and the other holds the floor.
            var left = new Slot(0, 0, 0, Layer.EdgeZ);
            var right = new Slot(2, 0, 0, Layer.EdgeZ);
            var pieces = new Dictionary<Slot, PieceKind>
            {
                [left] = PieceKind.Wall, [right] = PieceKind.Wall,
                [new Slot(0, 0, 1, Layer.Floor)] = PieceKind.Floor, [new Slot(1, 0, 1, Layer.Floor)] = PieceKind.Floor,
            };
            pieces.Remove(left);
            Assert.IsEmpty(BuildRules.Unsupported(pieces));
        }

        [Test]
        public void AimSnapsAboveWhenLookingUp()
        {
            // Eye height on the ground, aim point up in the next storey.
            Assert.AreEqual(1, Slot.Snapped(PieceKind.Wall, 1.5f, 4, -2.9f, 0, 0, 2).Level);
            Assert.AreEqual(0, Slot.Snapped(PieceKind.Wall, 1.5f, 4, -2.9f, 0, 0, 0).Level, "clamped");
        }

        [Test]
        public void FloorAtFeetWhenLookingAhead()
        {
            // Standing on the ground, looking level: the aim is at eye height, 1.7m up.
            Assert.AreEqual(0, Slot.Snapped(PieceKind.Floor, 1.5f, 1.7f, -3.5f, 0, 0, 2).Level);
            // Looking up, the aim climbs past head height and the floor goes on as a ceiling.
            Assert.AreEqual(1, Slot.Snapped(PieceKind.Floor, 1.5f, 2.6f, -3.3f, 0, 0, 2).Level);
        }

        /// A hillside for testing: the ground height at each grid square, 0 where unset.
        class Hill : IGround
        {
            public readonly Dictionary<(int, int), float> Heights = new();
            float Under(Slot slot)
            {
                float highest = 0;
                foreach (var cell in slot.Cells) highest = System.Math.Max(highest, Heights.TryGetValue((cell.X, cell.Z), out var h) ? h : 0);
                return highest;
            }
            public bool Supports(Slot slot)
            {
                float bottom = slot.Level * BuildGrid.Storey, gap = bottom - Under(slot);
                // Floors stand on stilts across up to a storey; everything else needs the ground at its base.
                return slot.Layer == Layer.Floor ? gap <= BuildGrid.Storey : gap <= 0.3f;
            }
            public bool Buries(Slot slot) => Under(slot) >= (slot.Level + 1) * BuildGrid.Storey;
        }

        [Test]
        public void BuildsOnAHillside()
        {
            var hill = new Hill();
            hill.Heights[(0, 0)] = 7; // A little over two storeys up.
            var pieces = new Dictionary<Slot, PieceKind>();
            // Storey 2 starts at 6.5m: the hilltop reaches it, so a floor there stands on the ground.
            Assert.IsTrue(BuildRules.CanPlace(PieceKind.Floor, new Slot(0, 0, 2, Layer.Floor), pieces, hill));
            // Storey 0 is buried in the hill.
            Assert.IsFalse(BuildRules.CanPlace(PieceKind.Floor, new Slot(0, 0, 0, Layer.Floor), pieces, hill));
            // Out on the flat beside it, storey 0 is fine.
            Assert.IsTrue(BuildRules.CanPlace(PieceKind.Floor, new Slot(3, 0, 0, Layer.Floor), pieces, hill));
        }

        [Test]
        public void FloorStandsOnStiltsAcrossAStorey()
        {
            var hill = new Hill();
            hill.Heights[(0, 0)] = 3.5f; // Just over a storey up.
            var pieces = new Dictionary<Slot, PieceKind>();
            // Storey 2 floor is 3m above the ground: within a storey, so stilts hold it.
            Assert.IsTrue(BuildRules.CanPlace(PieceKind.Floor, new Slot(0, 0, 2, Layer.Floor), pieces, hill));
            // A wall can't stand on stilts: 3m of air under it means it needs a neighbour.
            Assert.IsFalse(BuildRules.CanPlace(PieceKind.Wall, new Slot(0, 0, 2, Layer.EdgeX), pieces, hill));
        }

        [Test]
        public void CollapseOnAHillside()
        {
            var hill = new Hill();
            hill.Heights[(0, 0)] = 3.3f;
            // A floor on the hilltop at storey 1, a floor hanging off it to the side, nothing under that one.
            var top = new Slot(0, 0, 1, Layer.Floor);
            var hanging = new Slot(1, 0, 1, Layer.Floor);
            var pieces = new Dictionary<Slot, PieceKind> { [top] = PieceKind.Floor, [hanging] = PieceKind.Floor };
            Assert.IsEmpty(BuildRules.Unsupported(pieces, hill));
            pieces.Remove(top);
            // The flat beside the hill is 3.25m below storey 1: still within stilt reach, so it stays.
            Assert.IsEmpty(BuildRules.Unsupported(pieces, hill));
            // Two storeys up, nothing reaches it.
            var high = new Slot(1, 0, 2, Layer.Floor);
            Assert.IsTrue(BuildRules.Unsupported(new Dictionary<Slot, PieceKind> { [high] = PieceKind.Floor }, hill).Contains(high));
        }

        [Test]
        public void WallFacesThePlayer()
        {
            // Aim point right beside a square's side edge: the wall still goes across the view.
            Assert.AreEqual(Layer.EdgeX, Slot.Snapped(PieceKind.Wall, 0.1f, 1.7f, -2, 0, 0, 0).Layer);
            Assert.AreEqual(new Slot(0, -1, 0, Layer.EdgeZ), Slot.Snapped(PieceKind.Wall, 0.1f, 1.7f, -2, 1, 0, 0));
        }
    }
}
