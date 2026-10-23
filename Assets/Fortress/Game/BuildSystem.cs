using System.Collections;
using System.Collections.Generic;
using Fortress.Building;
using UnityEngine;

namespace Fortress.Game
{
    /// Marks a built piece's root object with the slot it fills.
    public class Piece : MonoBehaviour
    {
        public Slot Slot;
    }

    /// Everything built: the ghost preview, frames that are hammered into finished pieces, health,
    /// damage and collapse.
    public class BuildSystem : MonoBehaviour
    {
        /// How far out a piece goes when the crosshair isn't on anything closer.
        const float BuildDistance = 2.6f;
        /// How far the aim looks for something to build against.
        const float AimReach = 6;

        readonly Dictionary<Slot, PieceKind> kinds = new();
        readonly Dictionary<Slot, GameObject> nodes = new();
        readonly Dictionary<Slot, BuildMaterial> materials = new();
        readonly Dictionary<Slot, float> hp = new();
        /// Which way each ramp climbs, in quarter turns: 0 is toward -z, then -x, +z, +x.
        readonly Dictionary<Slot, int> headings = new();
        /// Frames still being built, and how far along each is, 0 to 1. Finished pieces aren't in here.
        readonly Dictionary<Slot, float> progress = new();
        /// The parts laid so far inside each frame, and the full list of parts it needs.
        readonly Dictionary<Slot, Transform> fills = new();
        readonly Dictionary<Slot, List<(Vector3 size, Vector3 position)>> partPlans = new();
        Material scaffold, frameShell;
        readonly Dictionary<Corner, GameObject> posts = new();
        Transform builtRoot, postRoot;
        GameObject ghost;
        PieceKind? ghostKind;
        Material ghostOk, ghostBad;

        public static int PieceLayer { get; private set; }
        /// The land the pieces stand on.
        public IGround Ground { get; set; } = FlatGround.Instance;

        void Awake()
        {
            PieceLayer = Mathf.Max(0, LayerMask.NameToLayer("Pieces"));
            builtRoot = new GameObject("Built").transform;
            postRoot = new GameObject("Posts").transform;
            ghostOk = GameAssets.Ghost(new Color(0.3f, 0.9f, 0.45f, 0.45f));
            ghostBad = GameAssets.Ghost(new Color(0.95f, 0.3f, 0.3f, 0.45f));
            scaffold = GameAssets.Art("Planks");
            frameShell = GameAssets.Ghost(new Color(0.95f, 0.9f, 0.8f, 0.22f));
        }

        /// Whether `slot` holds a frame that still needs hammering.
        public bool IsFrame(Slot slot) => progress.ContainsKey(slot);

        /// How finished the piece in `slot` is: 0 a bare frame, 1 done.
        public float Progress(Slot slot) => progress.TryGetValue(slot, out var done) ? done : 1;

        /// Hammers the frame in `slot` along by `amount` (a share of the whole build). Returns whether
        /// that finished it.
        public bool Hammer(Slot slot, float amount)
        {
            if (!progress.TryGetValue(slot, out var done) || !nodes.TryGetValue(slot, out var node)) return false;
            var stats = materials[slot].Stats();
            // Rounded up at the end: six sixths add up to 0.99999994 in floating point, not 1.
            done = done + amount >= 0.999f ? 1 : done + amount;
            hp[slot] = Mathf.Min(stats.MaxHP, hp[slot] + amount * stats.MaxHP);
            if (done < 1)
            {
                progress[slot] = done;
                ShowProgress(slot, done);
                StartCoroutine(Shake(node.transform));
                return false;
            }
            // Finished: the frame becomes the real thing.
            progress.Remove(slot);
            UpdatePosts(slot);
            if (fills.TryGetValue(slot, out var fill)) Destroy(fill.gameObject);
            fills.Remove(slot);
            partPlans.Remove(slot);
            var real = GameAssets.Piece(materials[slot]);
            foreach (Transform part in node.transform)
            {
                if (part.name == "Beam") Destroy(part.gameObject);
                else if (part.TryGetComponent<MeshRenderer>(out var renderer))
                {
                    renderer.sharedMaterial = real;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }
            return true;
        }

        /// Lays the parts a frame has earned so far, each settling into place as it goes in.
        void ShowProgress(Slot slot, float done)
        {
            if (!fills.TryGetValue(slot, out var fill) || !partPlans.TryGetValue(slot, out var plan)) return;
            int earned = done >= 1 ? plan.Count : Mathf.FloorToInt(done * plan.Count);
            var surface = GameAssets.Piece(materials[slot]);
            for (int i = fill.childCount; i < earned; i++)
            {
                var (size, position) = plan[i];
                var part = Box(fill, surface, size, position, solid: false);
                part.name = "Part";
                part.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                StartCoroutine(Settle(part.transform, (i - fill.childCount + 1) * 0.04f));
            }
        }

        /// A part dropping the last few centimetres into place, slightly staggered after the one before.
        static IEnumerator Settle(Transform part, float delay)
        {
            var home = part.localPosition;
            part.localScale = Vector3.zero;
            yield return new WaitForSeconds(delay);
            for (float t = 0; t < 0.12f && part; t += Time.deltaTime)
            {
                float k = t / 0.12f;
                part.localScale = Vector3.one * Mathf.Lerp(0.8f, 1, k);
                part.localPosition = home + Vector3.up * (0.06f * (1 - k));
                yield return null;
            }
            if (!part) yield break;
            part.localScale = Vector3.one;
            part.localPosition = home;
        }

        public IReadOnlyDictionary<Slot, PieceKind> Kinds => kinds;

        /// The slot a piece of `kind` goes in for a ray from the player's eye.
        public Slot AimedSlot(PieceKind kind, Ray aim, int bodyLevel, int heading)
        {
            float distance = BuildDistance;
            // Stop at the first thing in the way (a piece, the ground, a tree), a little in front of it,
            // so pieces go on the near side.
            if (Physics.Raycast(aim, out var hit, AimReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                distance = Mathf.Min(distance, hit.distance - 0.15f);
            var point = aim.GetPoint(Mathf.Max(distance, 0));
            var slot = Slot.Snapped(kind, point.x, point.y, point.z, heading, Mathf.Max(0, bodyLevel - 1), bodyLevel + 2);
            // Storeys count up from sea level, so on raised ground the storey you're looking into can
            // hang in the air. If it would float but the one below stands on the hill, build there.
            if (slot.Level > 0 && !BuildRules.CanPlace(kind, slot, kinds, Ground))
            {
                var below = slot.Moved(levels: -1);
                if (BuildRules.CanPlace(kind, below, kinds, Ground)) return below;
            }
            return slot;
        }

        /// Builds `kind` in `slot` if the rules allow it: as a frame that has to be hammered into the
        /// finished piece. `free` skips the cost and puts the piece up finished, for the world's own
        /// buildings. Returns whether it was placed.
        public bool Place(PieceKind kind, Slot slot, BuildMaterial material, int heading = 0, bool free = false)
        {
            // The world's own buildings are laid out by hand and skip the rules.
            if (kinds.ContainsKey(slot) || (!free && !BuildRules.CanPlace(kind, slot, kinds, Ground))) return false;
            var stats = material.Stats();
            if (!free && !Stock.Spend(material, stats.Cost)) return false;

            // A frame is a pale see-through shell (solid to stand on and build against) with wooden
            // beams along its edges; a finished piece is the shell in its real material.
            var node = MakePiece(kind, free ? GameAssets.Piece(material) : frameShell, solid: true);
            node.name = $"{material} {kind} {slot}";
            node.AddComponent<Piece>().Slot = slot;
            Position(node.transform, slot, heading);
            node.transform.SetParent(builtRoot, true);
            if (kind == PieceKind.Floor) AddStilts(node.transform, slot, GameAssets.Piece(material));
            if (kind == PieceKind.Ramp) headings[slot] = heading;
            kinds[slot] = kind;
            nodes[slot] = node;
            materials[slot] = material;
            if (free)
            {
                hp[slot] = stats.MaxHP;
            }
            else
            {
                // A bare frame is weak until it's hammered up.
                hp[slot] = stats.MaxHP * 0.1f;
                progress[slot] = 0;
                foreach (var renderer in node.GetComponentsInChildren<MeshRenderer>())
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                AddBeams(node.transform, kind);
                var fill = new GameObject("Parts").transform;
                fill.SetParent(node.transform, false);
                fills[slot] = fill;
                partPlans[slot] = BuildParts(kind, material);
            }
            UpdatePosts(slot);
            return true;
        }

        /// Wooden beams along a frame's outline, so it reads as scaffolding before it's built.
        void AddBeams(Transform frame, PieceKind kind)
        {
            const float beam = 0.12f;
            float span = BuildGrid.Unit - BuildGrid.WallThickness, storey = BuildGrid.Storey;
            void Beam(Vector3 from, Vector3 to)
            {
                var part = Box(frame, scaffold, new Vector3(beam, beam, Vector3.Distance(from, to) + beam), Vector3.zero, solid: false);
                part.name = "Beam";
                part.transform.localPosition = (from + to) / 2;
                part.transform.localRotation = Quaternion.LookRotation(to - from, Mathf.Abs((to - from).normalized.y) > 0.99f ? Vector3.forward : Vector3.up);
                part.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            switch (kind)
            {
                case PieceKind.Floor:
                {
                    float h = BuildGrid.Slab, e = BuildGrid.Unit / 2 - beam / 2;
                    var corners = new[] { new Vector3(-e, h, -e), new Vector3(e, h, -e), new Vector3(e, h, e), new Vector3(-e, h, e) };
                    for (int i = 0; i < 4; i++) Beam(corners[i], corners[(i + 1) % 4]);
                    Beam(corners[0], corners[2]);
                    break;
                }
                case PieceKind.Ramp:
                {
                    float x = BuildGrid.Unit / 2 - beam / 2, z = BuildGrid.Unit / 2, top = BuildGrid.Slab + storey;
                    // Two stringers up the slope and treads across them.
                    foreach (float side in new[] { -x, x }) Beam(new Vector3(side, BuildGrid.Slab, z), new Vector3(side, top, -z));
                    for (int i = 0; i <= 3; i++)
                    {
                        float t = i / 3f;
                        float y = Mathf.Lerp(BuildGrid.Slab, top, t), along = Mathf.Lerp(z, -z, t);
                        Beam(new Vector3(-x, y, along), new Vector3(x, y, along));
                    }
                    break;
                }
                default:
                {
                    // A wall, door or window frame: posts, top and bottom plates, and a diagonal brace.
                    float x = span / 2 - beam / 2, low = beam / 2, high = storey - beam / 2;
                    Beam(new Vector3(-x, low, 0), new Vector3(-x, high, 0));
                    Beam(new Vector3(x, low, 0), new Vector3(x, high, 0));
                    Beam(new Vector3(-x, low, 0), new Vector3(x, low, 0));
                    Beam(new Vector3(-x, high, 0), new Vector3(x, high, 0));
                    if (kind == PieceKind.Wall) Beam(new Vector3(-x, low, 0), new Vector3(x, high, 0));
                    break;
                }
            }
        }

        /// The built slot a hit collider belongs to, if it is part of a built piece.
        public bool TryGetSlot(Collider collider, out Slot slot)
        {
            var piece = collider ? collider.GetComponentInParent<Piece>() : null;
            slot = piece ? piece.Slot : default;
            return piece && nodes.ContainsKey(slot);
        }

        public BuildMaterial MaterialAt(Slot slot) => materials.TryGetValue(slot, out var material) ? material : BuildMaterial.Wood;

        /// Takes a piece out of the world. A falling piece drops and shrinks away instead of vanishing.
        public void Remove(Slot slot, bool falling)
        {
            if (!nodes.TryGetValue(slot, out var node)) return;
            kinds.Remove(slot);
            nodes.Remove(slot);
            headings.Remove(slot);
            hp.Remove(slot);
            materials.Remove(slot);
            progress.Remove(slot);
            fills.Remove(slot);
            partPlans.Remove(slot);
            if (falling)
            {
                foreach (var collider in node.GetComponentsInChildren<Collider>()) collider.enabled = false;
                StartCoroutine(Fall(node.transform));
            }
            else
            {
                Destroy(node);
            }
            UpdatePosts(slot);
        }

        static IEnumerator Fall(Transform node)
        {
            var start = node.position;
            var size = node.localScale;
            for (float t = 0; t < 0.4f; t += Time.deltaTime)
            {
                node.position = start + Vector3.down * (t / 0.4f);
                node.localScale = size * (1 - t / 0.4f);
                yield return null;
            }
            Destroy(node.gameObject);
        }

        /// Drops everything no longer joined to the ground. Returns whether anything fell.
        public bool Collapse()
        {
            var orphans = BuildRules.Unsupported(kinds, Ground);
            foreach (var slot in orphans) Remove(slot, falling: true);
            return orphans.Count > 0;
        }

        /// Returns whether the piece broke.
        public bool Damage(Slot slot, float amount)
        {
            if (!nodes.TryGetValue(slot, out var node)) return false;
            float health = hp[slot] - amount;
            if (health <= 0)
            {
                Remove(slot, falling: true);
                Collapse();
                return true;
            }
            hp[slot] = health;
            StartCoroutine(Shake(node.transform));
            return false;
        }

        static IEnumerator Shake(Transform node)
        {
            var home = node.position;
            for (float t = 0; t < 0.09f && node; t += Time.deltaTime)
            {
                node.position = home + Vector3.right * (0.04f * Mathf.Sin(t / 0.09f * Mathf.PI));
                yield return null;
            }
            if (node) node.position = home;
        }

        /// Fills the gap where wall ends meet with a post, at both ends of an edge slot.
        void UpdatePosts(Slot slot)
        {
            foreach (var corner in slot.Ends)
            {
                // The post takes the material of a finished wall meeting it, or is scaffold wood while
                // every wall there is still a frame.
                Slot? first = null, finished = null;
                foreach (var edge in BuildRules.EdgesMeetingAt(corner))
                {
                    if (!kinds.ContainsKey(edge)) continue;
                    first ??= edge;
                    if (finished == null && !progress.ContainsKey(edge)) finished = edge;
                }
                posts.TryGetValue(corner, out var post);
                if (first == null)
                {
                    if (post) Destroy(post);
                    posts.Remove(corner);
                    continue;
                }
                var look = finished is { } done ? GameAssets.Piece(materials[done]) : scaffold;
                if (!post)
                {
                    post = Box(postRoot, look, new Vector3(BuildGrid.WallThickness, BuildGrid.Storey, BuildGrid.WallThickness),
                               new Vector3(corner.X * BuildGrid.Unit, corner.Level * BuildGrid.Storey + BuildGrid.Storey / 2, corner.Z * BuildGrid.Unit), solid: true);
                    post.layer = PieceLayer;
                    post.name = "Post";
                    posts[corner] = post;
                }
                post.GetComponent<MeshRenderer>().sharedMaterial = look;
            }
        }

        /// Moves the ghost to where `kind` would go, green if it can be built there and red if not.
        public void ShowGhost(PieceKind? kind, Ray aim, int bodyLevel, int heading, bool affordable)
        {
            if (kind != ghostKind)
            {
                if (ghost) Destroy(ghost);
                ghost = kind is { } shape ? MakePiece(shape, ghostOk, solid: false) : null;
                if (ghost) ghost.name = "Ghost";
                ghostKind = kind;
            }
            if (!ghost || kind is not { } current) return;
            var slot = AimedSlot(current, aim, bodyLevel, heading);
            // Over a piece that's already there the ghost would only flicker against it.
            ghost.SetActive(!kinds.ContainsKey(slot));
            Position(ghost.transform, slot, heading);
            var surface = BuildRules.CanPlace(current, slot, kinds, Ground) && affordable ? ghostOk : ghostBad;
            foreach (var renderer in ghost.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = surface;
        }

        static void Position(Transform node, Slot slot, int heading)
        {
            float x = slot.X * BuildGrid.Unit, y = slot.Level * BuildGrid.Storey, z = slot.Z * BuildGrid.Unit;
            switch (slot.Layer)
            {
                case Layer.Floor:
                    node.SetPositionAndRotation(new Vector3(x + BuildGrid.Unit / 2, y, z + BuildGrid.Unit / 2), Quaternion.identity);
                    break;
                case Layer.Ramp:
                    node.SetPositionAndRotation(new Vector3(x + BuildGrid.Unit / 2, y, z + BuildGrid.Unit / 2), Quaternion.Euler(0, heading * 90, 0));
                    break;
                case Layer.EdgeX:
                    node.SetPositionAndRotation(new Vector3(x + BuildGrid.Unit / 2, y, z), Quaternion.identity);
                    break;
                case Layer.EdgeZ:
                    node.SetPositionAndRotation(new Vector3(x, y, z + BuildGrid.Unit / 2), Quaternion.Euler(0, 90, 0));
                    break;
            }
        }

        /// Builds a piece with its origin at the bottom of its storey, at the centre of its slot.
        /// Edge pieces run the full storey, down through the floor slab, so they meet the ground
        /// and the wall below; they stop short of the corners, where a post fills in.
        static GameObject MakePiece(PieceKind kind, Material material, bool solid)
        {
            var root = new GameObject(kind.ToString());
            if (solid) root.layer = PieceLayer;
            var t = root.transform;
            foreach (var (size, position, collide) in PieceBoxes(kind))
                Box(t, material, size, position, solid && collide);
            if (kind == PieceKind.Ramp && solid)
            {
                // Walked on as one smooth slope rather than step by step.
                float storey = BuildGrid.Storey, length = Mathf.Sqrt(BuildGrid.Unit * BuildGrid.Unit + storey * storey);
                var slope = new GameObject("Slope") { layer = PieceLayer };
                slope.transform.SetParent(t, false);
                slope.transform.localPosition = new Vector3(0, BuildGrid.Slab + storey / 2 - 0.1f, 0);
                // Positive x-rotation tips the +z end down, which raises the -z end the steps climb toward.
                slope.transform.localRotation = Quaternion.Euler(Mathf.Atan2(storey, BuildGrid.Unit) * Mathf.Rad2Deg, 0, 0);
                slope.AddComponent<BoxCollider>().size = new Vector3(BuildGrid.Unit, 0.2f, length);
            }
            return root;
        }

        /// The boxes a piece is made of, as (size, centre, has a collider), in the piece's own space.
        static List<(Vector3 size, Vector3 position, bool collide)> PieceBoxes(PieceKind kind)
        {
            var boxes = new List<(Vector3, Vector3, bool)>();
            float span = BuildGrid.Unit - BuildGrid.WallThickness, storey = BuildGrid.Storey, thick = BuildGrid.WallThickness;
            void Part(float w, float h, float d, float x, float y, float z = 0, bool collide = true) =>
                boxes.Add((new Vector3(w, h, d), new Vector3(x, y, z), collide));
            switch (kind)
            {
                case PieceKind.Floor:
                    Part(BuildGrid.Unit, BuildGrid.Slab, BuildGrid.Unit, 0, BuildGrid.Slab / 2);
                    break;
                case PieceKind.Ramp:
                    // Solid-looking steps climbing toward -z, from this storey's floor to the next one's.
                    const int steps = 8;
                    float rise = storey / steps, run = BuildGrid.Unit / steps;
                    for (int step = 0; step < steps; step++)
                    {
                        float height = BuildGrid.Slab + rise * (step + 1);
                        Part(BuildGrid.Unit, height, run, 0, height / 2, BuildGrid.Unit / 2 - run * (step + 0.5f), collide: false);
                    }
                    break;
                case PieceKind.Wall:
                    Part(span, storey, thick, 0, storey / 2);
                    break;
                case PieceKind.Door:
                {
                    float post = (span - BuildGrid.DoorWidth) / 2, lintel = 0.8f;
                    Part(post, storey, thick, -(BuildGrid.DoorWidth + post) / 2, storey / 2);
                    Part(post, storey, thick, (BuildGrid.DoorWidth + post) / 2, storey / 2);
                    Part(BuildGrid.DoorWidth, lintel, thick, 0, storey - lintel / 2);
                    break;
                }
                case PieceKind.Window:
                {
                    float opening = 1.2f, sill = BuildGrid.Slab + 1, top = storey - sill - 1, column = (span - opening) / 2;
                    Part(column, storey, thick, -(opening + column) / 2, storey / 2);
                    Part(column, storey, thick, (opening + column) / 2, storey / 2);
                    Part(opening, sill, thick, 0, sill / 2);
                    Part(opening, top, thick, 0, storey - top / 2);
                    break;
                }
            }
            return boxes;
        }

        /// The parts a piece is put together from while it's hammered, in the order they go in:
        /// bricks in rows (each row a left then a right half) and boards from the bottom up, metal
        /// as sheets left to right (each a lower then an upper half), floors across, ramps a step at a time.
        static List<(Vector3 size, Vector3 position)> BuildParts(PieceKind kind, BuildMaterial material)
        {
            var parts = new List<(Vector3 size, Vector3 position, float order)>();
            void Add(Vector3 size, Vector3 centre, float order) => parts.Add((size, centre, order));
            foreach (var (size, centre, _) in PieceBoxes(kind))
            {
                var min = centre - size / 2;
                if (kind == PieceKind.Ramp)
                {
                    // A step is a part; steps go in from the bottom.
                    Add(size, centre, centre.y);
                    continue;
                }
                // Floors are laid across their depth (z); everything else rises up its height (y).
                bool floor = kind == PieceKind.Floor;
                float length = floor ? size.z : size.y;
                if (material == BuildMaterial.Metal)
                {
                    // Sheets about 0.93m wide, each fixed as two halves.
                    int sheets = Mathf.Max(1, Mathf.RoundToInt(size.x / 0.93f));
                    float sheet = size.x / sheets;
                    for (int i = 0; i < sheets; i++)
                    for (int half = 0; half < 2; half++)
                    {
                        float x = min.x + sheet * (i + 0.5f);
                        var partSize = floor ? new Vector3(sheet, size.y, size.z / 2) : new Vector3(sheet, size.y / 2, size.z);
                        var partCentre = floor ? new Vector3(x, centre.y, min.z + size.z * (half + 0.5f) / 2)
                                               : new Vector3(x, min.y + size.y * (half + 0.5f) / 2, centre.z);
                        Add(partSize, partCentre, x * 10 + half);
                    }
                    continue;
                }
                // Brick rows a third of a metre deep, laid in two halves; wooden boards 0.22m, whole.
                float band = material == BuildMaterial.Brick ? 0.33f : 0.22f;
                int rows = Mathf.Max(1, Mathf.RoundToInt(length / band));
                float row = length / rows;
                int pieces = material == BuildMaterial.Brick && size.x > 1 ? 2 : 1;
                for (int r = 0; r < rows; r++)
                for (int p = 0; p < pieces; p++)
                {
                    float along = min.x + size.x * (p + 0.5f) / pieces;
                    var partSize = floor ? new Vector3(size.x / pieces, size.y, row) : new Vector3(size.x / pieces, row, size.z);
                    var partCentre = floor ? new Vector3(along, centre.y, min.z + row * (r + 0.5f))
                                           : new Vector3(along, min.y + row * (r + 0.5f), centre.z);
                    Add(partSize, partCentre, (floor ? partCentre.z : partCentre.y) * 10 + p * 0.01f);
                }
            }
            parts.Sort((a, b) => a.order.CompareTo(b.order));
            return parts.ConvertAll(part => (part.size, part.position));
        }

        /// Posts under a floor's corners down into the ground, where the floor stands clear of a slope.
        void AddStilts(Transform floor, Slot slot, Material material)
        {
            if (Ground is not TerrainGround terrain) return;
            float inset = BuildGrid.Unit / 2 - 0.15f;
            foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                var corner = floor.position + new Vector3(dx * inset, 0, dz * inset);
                float gap = terrain.GapUnder(corner.x, corner.z, slot.Level);
                // Stilts only bridge the up-to-a-storey gap the ground rules allow; higher floors
                // are held by what they're built on.
                if (gap < 0.05f || gap > BuildGrid.Storey + 0.05f) continue;
                // Sunk 0.4m into the ground so it looks planted on uneven earth.
                float length = gap + 0.4f;
                Box(floor, material, new Vector3(0.25f, length, 0.25f), new Vector3(dx * inset, -length / 2, dz * inset), solid: true);
            }
        }

        /// A box of `size` centred at `position` under `parent`, textured at real scale, with a
        /// collider if `solid`.
        public static GameObject Box(Transform parent, Material material, Vector3 size, Vector3 position, bool solid)
        {
            var box = new GameObject("Box") { layer = parent.gameObject.layer };
            box.AddComponent<MeshFilter>().sharedMesh = Meshes.Box(size, position);
            var renderer = box.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (solid) box.AddComponent<BoxCollider>().size = size;
            else renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            return box;
        }
    }
}
