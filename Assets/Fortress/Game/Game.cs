using System.Collections.Generic;
using Fortress.Building;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fortress.Game
{
    /// Builds the world at startup on the baked terrain: sky and light, forest, rocks, ferns, ruins,
    /// wrecks and crates, then the player with their tools and HUD.
    public class Game : MonoBehaviour
    {
        /// How far from the middle things are scattered; the edge walls stop the player here.
        const float WorldRadius = WorldShape.Extent - 30;

        /// Set before loading the scene (or pass `-demo` on the command line) to put up a small
        /// house and ramp chain and stand the player outside it, for screenshots.
        public static bool Demo;

        public Player Player { get; private set; }
        public Tools Tools { get; private set; }
        public BuildSystem Build { get; private set; }

        Terrain terrain;
        TerrainGround ground;
        List<Vector2> landmarkSpots;

        void Awake()
        {
            Stock.Reset();
            Random.InitState(20261018);
            terrain = FindFirstObjectByType<Terrain>();
            ground = new TerrainGround(terrain);
            Build = gameObject.AddComponent<BuildSystem>();
            Build.Ground = ground;
            BuildSky();
            landmarkSpots = MakeLandmarkSpots();
            BuildEdges();
            BuildForest();
            BuildRocks();
            for (int i = 0; i < landmarkSpots.Count; i++)
                AddCrate(i % 2 == 0 ? BuildRuin(landmarkSpots[i]) : BuildWreck(landmarkSpots[i]));

            var body = new GameObject("Player") { layer = 2 };
            body.transform.position = OnGround(0, 0) + Vector3.up * 0.1f;
            Player = body.AddComponent<Player>();
            var voice = body.AddComponent<AudioSource>();
            voice.spatialBlend = 0;
            Player.Setup(voice);
            // The iOS player started looking toward -z.
            Player.Face(180, 0);
            var rig = Player.View.gameObject.AddComponent<HeldRig>();
            rig.Setup(Player);
            Tools = gameObject.AddComponent<Tools>();
            Tools.Setup(Player, Build, rig, voice);
            new GameObject("HUD").AddComponent<Hud>().Setup(Player, Tools);

            var wind = gameObject.AddComponent<AudioSource>();
            wind.clip = GameAssets.Sound("wind");
            wind.loop = true;
            wind.volume = 0.3f;
            wind.Play();

            var arguments = System.Environment.GetCommandLineArgs();
            if (Demo || System.Array.IndexOf(arguments, "-demo") >= 0) BuildDemoHouse();
            if (System.Array.IndexOf(arguments, "-benchmark") >= 0) StartCoroutine(Benchmark());
        }

        /// `-benchmark`: looks out over the land and into the woods, logs the frame rate, and quits.
        System.Collections.IEnumerator Benchmark()
        {
            yield return new WaitForSecondsRealtime(4);
            foreach (var (view, yaw) in new[] { ("open land", 150f), ("toward the woods", 30f) })
            {
                Player.Face(yaw, 0);
                yield return new WaitForSecondsRealtime(1);
                int frames = 0;
                float start = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - start < 4)
                {
                    frames++;
                    yield return null;
                }
                Debug.Log($"BENCHMARK {view}: {frames / 4f:0} fps at {Screen.width}x{Screen.height}");
            }
            Application.Quit();
        }

        Vector3 OnGround(float x, float z) => new(x, ground.HeightAt(x, z), z);

        /// How steep the ground is at (x, z): 1 is flat, 0 is a cliff.
        float Flatness(float x, float z)
        {
            if (!terrain) return 1;
            var data = terrain.terrainData;
            var local = new Vector3(x, 0, z) - terrain.transform.position;
            return data.GetInterpolatedNormal(local.x / data.size.x, local.z / data.size.z).y;
        }

        void BuildSky()
        {
            // The sky is a photographed one; it also lights the world and gives reflections their colour.
            RenderSettings.skybox = Resources.Load<Material>("Materials/Sky");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();
            // Haze that thickens with distance, the colour of the horizon.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.68f, 0.75f, 0.84f);
            RenderSettings.fogDensity = 0.0042f;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1, 0.95f, 0.86f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            sun.transform.rotation = Quaternion.Euler(48, 140, 0);
            RenderSettings.sun = sun;

            var post = new GameObject("Post-processing").AddComponent<Volume>();
            post.isGlobal = true;
            post.sharedProfile = Resources.Load<VolumeProfile>("PostProfile");
        }

        /// Invisible walls just inside the terrain's edge.
        static void BuildEdges()
        {
            for (int side = 0; side < 4; side++)
            {
                var wall = new GameObject("Edge").AddComponent<BoxCollider>();
                bool alongX = side < 2;
                float sign = side % 2 == 0 ? 1 : -1;
                wall.transform.position = alongX ? new Vector3(0, 50, sign * WorldRadius) : new Vector3(sign * WorldRadius, 50, 0);
                wall.size = alongX ? new Vector3(WorldRadius * 2, 100, 1) : new Vector3(1, 100, WorldRadius * 2);
            }
        }

        /// Where the ruins and wrecks stand: two within sight of the start, the rest spread over the
        /// map, all on fairly level ground.
        List<Vector2> MakeLandmarkSpots()
        {
            var spots = new List<Vector2>();
            foreach (var near in new[] { new Vector2(-18, -15), new Vector2(34, 10) }) spots.Add(LevelSpotNear(near));
            while (spots.Count < 20)
            {
                var point = ScatterPoint();
                var spot = new Vector2(point.x, point.z);
                if (spot.magnitude > 70 && Flatness(spot.x, spot.y) > 0.97f) spots.Add(spot);
            }
            return spots;
        }

        /// The flattest ground within 30m of `spot`.
        Vector2 LevelSpotNear(Vector2 spot)
        {
            var best = spot;
            float flattest = Flatness(spot.x, spot.y);
            for (int i = 0; i < 60; i++)
            {
                var candidate = spot + Random.insideUnitCircle * 30;
                float flat = Flatness(candidate.x, candidate.y);
                if (flat > flattest) (best, flattest) = (candidate, flat);
            }
            return best;
        }

        bool NearLandmark(Vector3 point, float distance) =>
            landmarkSpots.Exists(spot => Vector2.Distance(spot, new Vector2(point.x, point.z)) < distance);

        /// A random ground point that leaves a clearing around the start.
        static Vector3 ScatterPoint()
        {
            // Square root keeps the spread even by area instead of bunching at the centre.
            float angle = Random.Range(0, Mathf.PI * 2);
            float radius = Mathf.Max(16, Mathf.Sqrt(Random.value) * WorldRadius);
            return new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
        }

        void BuildForest()
        {
            var materials = new[] { GameAssets.Art("Bark"), GameAssets.Art("Branch") };
            var kinds = new List<(Mesh near, Mesh far, float height)>();
            foreach (var (seed, height) in new[] { (1, 14f), (2, 18f), (3, 22f) })
                kinds.Add((Trees.Conifer(seed, height), Trees.Conifer(seed + 100, height, 0.45f), height));
            var ferns = new[] { "Fern_a", "Fern_b", "Fern_c", "Fern_d" };

            int trees = 0, attempts = 0, fernCount = 0;
            while (trees < 1800 && attempts++ < 40000)
            {
                var point = ScatterPoint();
                float forest = WorldShape.Forest(point.x, point.z);
                // Mostly in the woods, a few lone trees out in the open; never on cliffs or ruins.
                if (Random.value > 0.06f + 0.94f * forest || Flatness(point.x, point.z) < 0.85f || NearLandmark(point, 12)) continue;
                var kind = kinds[Random.Range(0, kinds.Count)];
                float scale = Random.Range(0.85f, 1.2f);
                var tree = Trees.Make(kind.near, kind.far, materials, kind.height * scale);
                tree.transform.position = OnGround(point.x, point.z) + Vector3.down * 0.3f;
                tree.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
                tree.transform.localScale = Vector3.one * scale;
                var source = tree.AddComponent<Source>();
                source.Material = BuildMaterial.Wood;
                source.Hits = 5;
                trees++;

                // Ferns gather in the shade under the trees.
                if (forest > 0.4f && Random.value < 0.7f)
                {
                    var spot = point + new Vector3(Random.Range(-4f, 4f), 0, Random.Range(-4f, 4f));
                    var fern = Instantiate(GameAssets.Model(ferns[Random.Range(0, ferns.Length)]));
                    fern.transform.position = OnGround(spot.x, spot.z);
                    fern.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
                    fern.transform.localScale = Vector3.one * Random.Range(0.8f, 1.4f);
                    fernCount++;
                }
            }
            Debug.Log($"Forest: {trees} trees, {fernCount} ferns");
        }

        void BuildRocks()
        {
            // Every scanned rock, sorted by kind from its name (see Editor/WorldSetup.cs).
            var all = Resources.LoadAll<GameObject>("Models/Rocks");
            GameObject[] Kind(string prefix) => System.Array.FindAll(all, rock => rock.name.StartsWith(prefix));
            var mossy = Kind("MossRocks");
            var boulders = Kind("Boulder");
            var pebbles = Kind("Rock0");
            int placed = 0;
            for (int attempt = 0; attempt < 6000 && placed < 280; attempt++)
            {
                var point = ScatterPoint();
                if (NearLandmark(point, 12)) continue;
                float forest = WorldShape.Forest(point.x, point.z), height = ground.HeightAt(point.x, point.z);
                // Mossy rocks under the trees, boulders out in the open (bigger on high ground), and
                // the old pebble scans blown up as the odd lone stone.
                GameObject[] kind;
                float scale, sink;
                if (forest > 0.5f && Random.value < 0.6f) (kind, scale, sink) = (mossy, Random.Range(0.7f, 1.6f), 0.15f);
                else if (Random.value < 0.8f) (kind, scale, sink) = (boulders, Random.Range(0.8f, height > 24 ? 3.2f : 2.4f), 0.12f);
                else (kind, scale, sink) = (pebbles, Random.Range(7f, 20f), 0.1f);
                if (kind.Length == 0) continue;
                var prefab = kind[Random.Range(0, kind.Length)];
                // The small boulder scan needs more size to stand among the others.
                if (prefab.name.StartsWith("Boulder06")) scale *= 2;
                var rock = Instantiate(prefab);
                rock.transform.localScale = Vector3.one * scale;
                rock.transform.rotation = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0, 360f), Random.Range(-10f, 10f));
                // Sunk into the earth by part of its height, so it looks settled, not dropped.
                var bounds = rock.GetComponentInChildren<Renderer>().bounds;
                rock.transform.position = OnGround(point.x, point.z) + Vector3.down * bounds.size.y * sink;
                var source = rock.AddComponent<Source>();
                source.Material = BuildMaterial.Brick;
                source.Hits = 4;
                placed++;
            }
            Debug.Log($"Rocks: {placed} ({mossy.Length} mossy, {boulders.Length} boulder, {pebbles.Length} pebble kinds)");
        }

        /// The storey that best fits the ground under `cells`: its floor nearest the middle height,
        /// so a building on a slope digs in a little uphill and stands on short stilts downhill.
        int BaseLevel(IEnumerable<Slot> cells)
        {
            float lowest = float.MaxValue, highest = 0;
            foreach (var cell in cells)
            {
                var (low, high) = ground.Under(cell);
                lowest = Mathf.Min(lowest, low);
                highest = Mathf.Max(highest, high);
            }
            return Mathf.Max(0, Mathf.RoundToInt(((lowest + highest) / 2 - BuildGrid.Slab) / BuildGrid.Storey));
        }

        /// A half-collapsed brick cabin made of ordinary building pieces. Returns where its crate sits.
        Vector3 BuildRuin(Vector2 spot)
        {
            int x = Mathf.FloorToInt(spot.x / BuildGrid.Unit), z = Mathf.FloorToInt(spot.y / BuildGrid.Unit);
            var cells = new List<Slot>();
            for (int dx = 0; dx <= 1; dx++)
            for (int dz = 0; dz <= 1; dz++) cells.Add(new Slot(x + dx, z + dz, 0, Layer.Floor));
            int level = BaseLevel(cells);
            void Put(PieceKind kind, int px, int pz, int up, Layer layer, BuildMaterial material) =>
                Build.Place(kind, new Slot(px, pz, level + up, layer), material, free: true);
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dz = 0; dz <= 1; dz++) Put(PieceKind.Floor, x + dx, z + dz, 0, Layer.Floor, BuildMaterial.Wood);
                Put(PieceKind.Wall, x + dx, z, 0, Layer.EdgeX, BuildMaterial.Brick);
            }
            for (int dz = 0; dz <= 1; dz++) Put(PieceKind.Wall, x, z + dz, 0, Layer.EdgeZ, BuildMaterial.Brick);
            Put(PieceKind.Wall, x + 2, z, 0, Layer.EdgeZ, BuildMaterial.Brick);
            Put(PieceKind.Door, x, z + 2, 0, Layer.EdgeX, BuildMaterial.Brick);
            for (int dx = 0; dx <= 1; dx++) Put(PieceKind.Floor, x + dx, z, 1, Layer.Floor, BuildMaterial.Wood);
            return new Vector3((x + 0.5f) * BuildGrid.Unit, level * BuildGrid.Storey + BuildGrid.Slab, (z + 0.5f) * BuildGrid.Unit);
        }

        /// A car left under a tarp, slowly rusting. Returns where its crate sits.
        Vector3 BuildWreck(Vector2 spot)
        {
            var car = Instantiate(GameAssets.Model("Car"));
            var position = OnGround(spot.x, spot.y);
            car.transform.position = position;
            car.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
            var source = car.AddComponent<Source>();
            source.Material = BuildMaterial.Metal;
            source.Hits = 8;
            var beside = car.transform.TransformPoint(new Vector3(2.4f, 0, 0));
            return OnGround(beside.x, beside.z);
        }

        static void AddCrate(Vector3 position)
        {
            var crate = Instantiate(GameAssets.Model("Crate"));
            crate.name = "Crate";
            crate.transform.position = position;
            crate.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
            crate.transform.localScale = Vector3.one * 1.3f;
            crate.AddComponent<Crate>();

            // A tall marker so a crate can be spotted from across the map.
            var beam = new GameObject("Beam") { layer = 2 };
            beam.AddComponent<MeshFilter>().sharedMesh = GameAssets.Cylinder;
            var glow = beam.AddComponent<MeshRenderer>();
            glow.sharedMaterial = GameAssets.Ghost(new Color(1, 0.85f, 0.4f, 0.35f));
            glow.shadowCastingMode = ShadowCastingMode.Off;
            beam.transform.position = position + Vector3.up * 12;
            beam.transform.localScale = new Vector3(0.14f, 12, 0.14f);
        }

        void BuildDemoHouse()
        {
            // Out on the open ground a little in front of the start, at whatever height the land is there.
            var cells = new List<Slot>();
            for (int x = -1; x <= 5; x++)
            for (int z = -3; z <= -2; z++) cells.Add(new Slot(x, z, 0, Layer.Floor));
            int level = BaseLevel(cells);
            void Put(PieceKind kind, int x, int z, int up, Layer layer, BuildMaterial material, int heading = 0) =>
                Build.Place(kind, new Slot(x, z, level + up, layer), material, heading, free: true);
            for (int x = -1; x <= 0; x++)
            {
                for (int z = -3; z <= -2; z++) Put(PieceKind.Floor, x, z, 0, Layer.Floor, BuildMaterial.Wood);
                Put(PieceKind.Wall, x, -3, 0, Layer.EdgeX, BuildMaterial.Brick);
            }
            Put(PieceKind.Door, -1, -1, 0, Layer.EdgeX, BuildMaterial.Brick);
            Put(PieceKind.Window, 0, -1, 0, Layer.EdgeX, BuildMaterial.Brick);
            for (int z = -3; z <= -2; z++)
            {
                Put(PieceKind.Wall, -1, z, 0, Layer.EdgeZ, BuildMaterial.Brick);
                Put(PieceKind.Wall, 1, z, 0, Layer.EdgeZ, z == -2 ? BuildMaterial.Metal : BuildMaterial.Brick);
                for (int x = -1; x <= 0; x++) Put(PieceKind.Floor, x, z, 1, Layer.Floor, BuildMaterial.Wood);
            }
            // A ramp chain climbing three storeys beside the house, onto a lookout floor.
            for (int step = 0; step < 3; step++) Put(PieceKind.Ramp, 2 + step, -2, step, Layer.Ramp, BuildMaterial.Wood, heading: 3);
            Put(PieceKind.Floor, 5, -2, 3, Layer.Floor, BuildMaterial.Metal);
            var controller = Player.GetComponent<CharacterController>();
            controller.enabled = false;
            Player.transform.position = OnGround(8, 8) + Vector3.up * 0.1f;
            controller.enabled = true;
            // iOS yaw 0.28 rad, looking toward -z and a little to the left.
            Player.Face(196, 3);
        }
    }
}
