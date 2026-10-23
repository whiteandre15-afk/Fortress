using System.IO;
using System.Linq;
using Fortress.Game;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// Turns the downloaded scans (Assets/Fortress/Art, from Tools/fetch_assets.py) into what the
    /// game loads: URP materials, ready-to-place model prefabs, the sky, and the baked terrain.
    public static class WorldSetup
    {
        const string Art = "Assets/Fortress/Art";
        const string Materials = "Assets/Fortress/Resources/Materials/Art";
        const string Models = "Assets/Fortress/Resources/Models";
        const string World = "Assets/Fortress/World";
        /// Scanned rocks, each made into game-ready pieces by Tools/reduce_models.py (or shipped that way).
        static readonly string[] RockScans = { "Rock07", "Rock09", "MossRocksA", "MossRocksB", "Boulder02", "Boulder03",
                                               "Boulder04", "Boulder06" };

        public static void Run()
        {
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory(Models);
            Directory.CreateDirectory(World);
            AssetDatabase.Refresh();
            foreach (var name in new[] { "Brick", "Planks", "Metal", "Steel", "Bark", "Grass", "ForestFloor", "RockGround", "Mud",
                                         "Rifle", "Rifle_Accesories", "Crate", "Car" }.Concat(RockScans))
                Surface(name, foliage: false);
            Surface("Branch", foliage: true);
            Surface("Fern", foliage: true);
            Glass();
            ModelPrefabs();
            Sky();
        }

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        /// A URP Lit material from a scan's albedo, normal and mask maps. Foliage is cut out by its
        /// alpha and drawn from both sides.
        static Material Surface(string name, bool foliage)
        {
            string folder = $"{Art}/{name.Split('_')[0]}";
            var albedo = Load<Texture2D>($"{folder}/{name}_albedo.png");
            if (!albedo) throw new System.Exception($"missing art for {name}: run Tools/fetch_assets.py and Tools/make_foliage.py");
            string path = $"{Materials}/{name}.mat";
            var material = Load<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", Color.white);
            var normal = Load<Texture2D>($"{folder}/{name}_normal.png");
            material.SetTexture("_BumpMap", normal);
            var mask = Load<Texture2D>($"{folder}/{name}_mask.png");
            if (mask)
            {
                // R metallic, G occlusion, A smoothness: one map feeds both slots.
                material.SetTexture("_MetallicGlossMap", mask);
                material.SetTexture("_OcclusionMap", mask);
                material.SetFloat("_Smoothness", 1);
                material.SetFloat("_OcclusionStrength", 1);
            }
            else
            {
                material.SetFloat("_Smoothness", 0.12f);
                material.SetFloat("_Metallic", 0);
            }
            material.SetFloat("_AlphaClip", foliage ? 1 : 0);
            material.SetFloat("_Cutoff", 0.45f);
            material.SetFloat("_Cull", foliage ? 0 : 2);
            material.SetFloat("_AlphaToMask", foliage ? 1 : 0);
            material.enableInstancing = true;
            // Let URP turn those settings into the right shader keywords and render queue.
            BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// The rifle scope's lens: dark, glossy, see-through.
        static void Glass()
        {
            string path = $"{Materials}/Glass.mat";
            var glass = Load<Material>(path);
            if (!glass)
            {
                glass = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(glass, path);
            }
            glass.SetColor("_BaseColor", new Color(0.05f, 0.07f, 0.08f, 0.6f));
            glass.SetFloat("_Smoothness", 0.95f);
            glass.SetFloat("_Surface", 1);
            BaseShaderGUI.SetMaterialKeywords(glass, LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(glass);
        }

        static Material Mat(string name) => Load<Material>($"{Materials}/{name}.mat");

        /// Saves a copy of an imported model as a prefab in Resources, with our materials on it.
        /// `keep` filters which parts of the file to keep; `collide` adds colliders.
        static void Prefab(string file, string name, System.Func<string, Material> materialFor,
                           System.Func<string, bool> keep = null, bool collide = false, Vector3? recentre = null)
        {
            var model = Load<GameObject>($"{Art}/{file}/{file}.fbx");
            var instance = (GameObject)Object.Instantiate(model);
            instance.name = name;
            foreach (Transform part in instance.transform.Cast<Transform>().ToList())
            {
                if (keep != null && !keep(part.name)) Object.DestroyImmediate(part.gameObject);
                else if (recentre is { } offset) part.localPosition -= offset;
            }
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => materialFor(m ? m.name : "")).ToArray();
            if (collide)
            {
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }
            PrefabUtility.SaveAsPrefabAsset(instance, $"{Models}/{name}.prefab");
            Object.DestroyImmediate(instance);
        }

        static void ModelPrefabs()
        {
            Prefab("Rifle", "Rifle", source => source.Contains("glass") ? Mat("Glass") : source.Contains("accesories") ? Mat("Rifle_Accesories") : Mat("Rifle"));
            // The file holds two crates side by side, one opened; keep the closed one, centred.
            Prefab("Crate", "Crate", _ => Mat("Crate"), keep: part => part.EndsWith("_a"), collide: true, recentre: new Vector3(0.5f, 0, 0));
            Prefab("Car", "Car", _ => Mat("Car"), collide: true);
            foreach (var letter in new[] { "a", "b", "c", "d" })
            {
                // Four ferns laid out on a grid in one file: one prefab each, moved to the middle.
                Vector3 at = letter switch { "a" => new(0, 0, 1), "c" => new(-1, 0, 0), "d" => new(-1, 0, 1), _ => Vector3.zero };
                Prefab("Fern", "Fern_" + letter, _ => Mat("Fern"), keep: part => part.EndsWith("_" + letter), recentre: at);
            }
            // Every rock model file, one prefab each, in Models/Rocks: the game sorts them by name.
            AssetDatabase.DeleteAsset($"{Models}/Rock07.prefab");
            AssetDatabase.DeleteAsset($"{Models}/Rock09.prefab");
            Directory.CreateDirectory($"{Models}/Rocks");
            foreach (var scan in RockScans)
            foreach (var file in Directory.GetFiles($"{Art}/{scan}", "*.fbx"))
                RockPrefab(scan, file.Replace('\\', '/'));
        }

        /// A rock with its detail levels switching by distance, colliding on the second.
        static void RockPrefab(string scan, string path)
        {
            var model = Load<GameObject>(path);
            var instance = (GameObject)Object.Instantiate(model);
            string name = Path.GetFileNameWithoutExtension(path);
            instance.name = name;
            var levels = instance.GetComponentsInChildren<Renderer>().OrderBy(r => r.name).ToArray();
            foreach (var renderer in levels) renderer.sharedMaterial = Mat(scan);
            var heights = new[] { 0.3f, 0.1f, 0.025f, 0.008f };
            // Unity already makes a LOD group from the _LOD0..3 part names; set its switch distances.
            // (An explicit check: Unity's "missing component" isn't a real null, so ?? would skip the add.)
            var group = instance.GetComponent<LODGroup>();
            if (!group) group = instance.AddComponent<LODGroup>();
            group.SetLODs(levels.Select((r, i) => new LOD(heights[Mathf.Min(i, 3)], new[] { r })).ToArray());
            var collisionLevel = levels[Mathf.Min(1, levels.Length - 1)];
            collisionLevel.gameObject.AddComponent<MeshCollider>().sharedMesh = collisionLevel.GetComponent<MeshFilter>().sharedMesh;
            PrefabUtility.SaveAsPrefabAsset(instance, $"{Models}/Rocks/{name}.prefab");
            Object.DestroyImmediate(instance);
        }

        static void Sky()
        {
            string path = "Assets/Fortress/Resources/Materials/Sky.mat";
            var sky = Load<Material>(path);
            var shader = Shader.Find("Skybox/Cubemap");
            if (!sky)
            {
                sky = new Material(shader);
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.shader = shader;
            // Set here as well as in ArtImport: an asset imported before that rule compiled keeps
            // its old settings until something reimports it.
            string hdr = $"{Art}/Sky/Sky.hdr";
            var importer = (TextureImporter)AssetImporter.GetAtPath(hdr);
            if (importer.textureShape != TextureImporterShape.TextureCube)
            {
                importer.textureShape = TextureImporterShape.TextureCube;
                importer.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                importer.SaveAndReimport();
            }
            var cubemap = Load<Cubemap>(hdr);
            if (!cubemap) throw new System.Exception("the sky HDR didn't import as a cubemap");
            sky.SetTexture("_Tex", cubemap);
            sky.SetFloat("_Exposure", 1);
            sky.SetFloat("_Rotation", 0);
            EditorUtility.SetDirty(sky);
        }

        /// Bakes the hills from WorldShape into a terrain, painted by slope and noise, with grass.
        public static Terrain BuildTerrain()
        {
            string dataPath = $"{World}/Terrain.asset";
            AssetDatabase.DeleteAsset(dataPath);
            var data = new TerrainData { heightmapResolution = 1025 };
            data.size = new Vector3(WorldShape.Extent * 2, WorldShape.MaxHeight, WorldShape.Extent * 2);
            int res = data.heightmapResolution;
            var heights = new float[res, res];
            for (int row = 0; row < res; row++)
            for (int column = 0; column < res; column++)
            {
                float x = -WorldShape.Extent + column / (float)(res - 1) * WorldShape.Extent * 2;
                float z = -WorldShape.Extent + row / (float)(res - 1) * WorldShape.Extent * 2;
                heights[row, column] = WorldShape.Height(x, z) / WorldShape.MaxHeight;
            }
            data.SetHeights(0, 0, heights);

            var layers = new[] { ("Grass", 3f), ("ForestFloor", 4f), ("RockGround", 7f), ("Mud", 4f) }.Select(spec =>
            {
                var (name, tile) = spec;
                var layer = new TerrainLayer
                {
                    diffuseTexture = Load<Texture2D>($"{Art}/{name}/{name}_albedo.png"),
                    normalMapTexture = Load<Texture2D>($"{Art}/{name}/{name}_normal.png"),
                    maskMapTexture = Load<Texture2D>($"{Art}/{name}/{name}_mask.png"),
                    tileSize = new Vector2(tile, tile),
                };
                // The grass scan is a late-summer meadow; nudge it a little greener.
                if (name == "Grass") layer.diffuseRemapMax = new Vector4(0.9f, 1.08f, 0.78f, 1);
                string layerPath = $"{World}/{name}.terrainlayer";
                AssetDatabase.DeleteAsset(layerPath);
                AssetDatabase.CreateAsset(layer, layerPath);
                return layer;
            }).ToArray();
            data.terrainLayers = layers;

            // Paint: rock on steep slopes, forest floor in the woods, mud in patches, grass elsewhere.
            data.alphamapResolution = 1024;
            int paint = data.alphamapResolution;
            var weights = new float[paint, paint, 4];
            var grassDensity = new float[paint, paint];
            for (int row = 0; row < paint; row++)
            for (int column = 0; column < paint; column++)
            {
                float u = column / (float)(paint - 1), v = row / (float)(paint - 1);
                float x = -WorldShape.Extent + u * WorldShape.Extent * 2, z = -WorldShape.Extent + v * WorldShape.Extent * 2;
                float steep = data.GetSteepness(u, v);
                float rock = Mathf.SmoothStep(0, 1, (steep - 20) / 14);
                float forest = WorldShape.Forest(x, z) * (1 - rock);
                float mud = WorldShape.Mud(x, z) * (1 - rock) * 0.8f;
                float grass = Mathf.Max(0, 1 - rock - forest - mud);
                float total = grass + forest + rock + mud;
                weights[row, column, 0] = grass / total;
                weights[row, column, 1] = forest / total;
                weights[row, column, 2] = rock / total;
                weights[row, column, 3] = mud / total;
                grassDensity[row, column] = grass / total;
            }
            data.SetAlphamaps(0, 0, weights);

            // Waving grass tufts where the grass layer shows.
            data.SetDetailResolution(1024, 32);
            // Values below are tufts per cell, not Unity 6's default 0-255 coverage.
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            // Photographed grass from Tools/make_foliage.py: low tufts first, then taller clumps.
            var grassFiles = Directory.GetFiles($"{Art}/GrassTuft", "GrassTuft_*_albedo.png").OrderBy(f => f).ToArray();
            int lowTufts = grassFiles.Length - 3;
            data.detailPrototypes = grassFiles.Select((file, index) =>
            {
                bool tall = index >= lowTufts;
                return new DetailPrototype
                {
                    prototypeTexture = Load<Texture2D>(file.Replace('\\', '/')),
                    renderMode = DetailRenderMode.Grass,
                    // Near white: the photos carry their own colour; this only shifts patches greener or drier.
                    healthyColor = new Color(0.92f, 0.98f, 0.86f),
                    dryColor = new Color(1, 0.94f, 0.8f),
                    minWidth = tall ? 0.45f : 0.55f, maxWidth = tall ? 0.8f : 1f,
                    minHeight = tall ? 0.5f : 0.3f, maxHeight = tall ? 0.95f : 0.55f,
                    noiseSpread = 2,
                };
            }).ToArray();
            int detail = data.detailResolution;
            var random = new System.Random(7);
            var tuftCounts = new int[grassFiles.Length][,];
            for (int i = 0; i < tuftCounts.Length; i++) tuftCounts[i] = new int[detail, detail];
            for (int row = 0; row < detail; row++)
            for (int column = 0; column < detail; column++)
            {
                float share = grassDensity[row * paint / detail, column * paint / detail];
                if (share < 0.35f) continue;
                // Patchy: thick in places, thin in others, so it never reads as planted rows. Tall
                // clumps gather in their own patches, the way long grass grows in clusters.
                float x = -WorldShape.Extent + column / (float)(detail - 1) * WorldShape.Extent * 2;
                float z = -WorldShape.Extent + row / (float)(detail - 1) * WorldShape.Extent * 2;
                float patch = Mathf.PerlinNoise(x / 9 + 3.3f, z / 9 + 8.1f);
                float longGrass = Mathf.SmoothStep(0, 1, (Mathf.PerlinNoise(x / 22 + 41.7f, z / 22 + 12.9f) - 0.5f) / 0.2f);
                int low = (int)(share * patch * 9 + random.NextDouble() * 2);
                for (int k = 0; k < low; k++) tuftCounts[random.Next(lowTufts)][row, column]++;
                int high = (int)(share * (0.3f + longGrass) * 6 + random.NextDouble() * 2);
                for (int k = 0; k < high; k++) tuftCounts[lowTufts + random.Next(3)][row, column]++;
            }
            for (int i = 0; i < tuftCounts.Length; i++) data.SetDetailLayer(0, 0, i, tuftCounts[i]);
            data.wavingGrassStrength = 0.35f;
            data.wavingGrassAmount = 0.35f;
            data.wavingGrassSpeed = 0.4f;
            data.wavingGrassTint = new Color(0.8f, 0.85f, 0.7f);
            AssetDatabase.CreateAsset(data, dataPath);

            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Terrain";
            terrainObject.transform.position = new Vector3(-WorldShape.Extent, 0, -WorldShape.Extent);
            var terrain = terrainObject.GetComponent<Terrain>();
            var lit = Load<Material>("Assets/Fortress/Rendering/TerrainLit.mat");
            if (!lit)
            {
                lit = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
                AssetDatabase.CreateAsset(lit, "Assets/Fortress/Rendering/TerrainLit.mat");
            }
            terrain.materialTemplate = lit;
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4;
            terrain.basemapDistance = 250;
            terrain.detailObjectDistance = 90;
            terrain.detailObjectDensity = 1;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            return terrain;
        }
    }
}
