using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Game
{
    /// Conifers made in code from scanned bark and a branch card cut from scanned pine twigs:
    /// a tapered trunk, and whorls of branches that shorten toward the top into a spire.
    public static class Trees
    {
        /// A pine `height` metres tall: submesh 0 is the trunk (bark), submesh 1 the branches.
        /// `branchShare` thins the branches out, for the far detail level.
        public static Mesh Conifer(int seed, float height, float branchShare = 1)
        {
            var random = new System.Random(seed);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var bark = new List<int>();
            var needles = new List<int>();

            // Trunk: eight sides, tapering to a point. Bark repeats every 2m up and twice around.
            const int sides = 8;
            float baseRadius = height * 0.022f;
            for (int ring = 0; ring <= 6; ring++)
            {
                float t = ring / 6f, y = t * height, radius = Mathf.Lerp(baseRadius, 0.02f, t);
                for (int side = 0; side <= sides; side++)
                {
                    float angle = side * Mathf.PI * 2 / sides;
                    var outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    vertices.Add(outward * radius + Vector3.up * y);
                    normals.Add(outward);
                    uvs.Add(new Vector2(side / (float)sides * 2, y / 2));
                }
            }
            for (int ring = 0; ring < 6; ring++)
            for (int side = 0; side < sides; side++)
            {
                int a = ring * (sides + 1) + side, b = a + sides + 1;
                bark.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }

            // Branches: whorls from a quarter of the way up to the top, longest at the bottom.
            int whorls = Mathf.RoundToInt(height * 1.1f);
            float maxLength = height * 0.27f;
            for (int whorl = 0; whorl < whorls; whorl++)
            {
                float t = whorl / (float)(whorls - 1);
                float y = Mathf.Lerp(height * 0.22f, height * 0.97f, t);
                float length = maxLength * Mathf.Pow(1 - t, 0.85f) + 0.6f;
                int count = Mathf.RoundToInt(Range(5, 8) * branchShare);
                float spin = Range(0, 360);
                for (int i = 0; i < count; i++)
                {
                    float yaw = (spin + i * 360f / count + Range(-18, 18)) * Mathf.Deg2Rad;
                    var outward = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
                    // Lower branches droop more under their weight.
                    float droop = Mathf.Lerp(28, 8, t) + Range(-6, 6);
                    var along = (outward * Mathf.Cos(droop * Mathf.Deg2Rad) + Vector3.down * Mathf.Sin(droop * Mathf.Deg2Rad)).normalized;
                    var across = Vector3.Cross(Vector3.up, outward).normalized;
                    // Two cards per branch, tilted differently around the branch, so it has body from any side.
                    foreach (float roll in new[] { Range(-25, 25), Range(55, 80) * (random.Next(2) * 2 - 1) })
                    {
                        var side = Quaternion.AngleAxis(roll, along) * across;
                        float width = length * 0.3f;
                        var root = Vector3.up * y;
                        int start = vertices.Count;
                        vertices.Add(root - side * width / 2);
                        vertices.Add(root + side * width / 2);
                        vertices.Add(root + along * length + side * width / 2);
                        vertices.Add(root + along * length - side * width / 2);
                        // Light the needles as if the whole crown were round: facing out and up.
                        var light = (outward + Vector3.up * 0.7f).normalized;
                        for (int k = 0; k < 4; k++) normals.Add(light);
                        uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
                        needles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                    }
                }
            }
            // A spire of upright cards at the very top.
            for (int i = 0; i < 3; i++)
            {
                float yaw = i * 60 * Mathf.Deg2Rad;
                var side = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
                float tip = height * 0.12f, width = tip * 0.35f;
                var root = Vector3.up * (height * 0.9f);
                int start = vertices.Count;
                vertices.Add(root - side * width / 2);
                vertices.Add(root + side * width / 2);
                vertices.Add(root + Vector3.up * tip + side * width / 2);
                vertices.Add(root + Vector3.up * tip - side * width / 2);
                for (int k = 0; k < 4; k++) normals.Add(Vector3.up);
                uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
                needles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            var mesh = new Mesh { name = $"Conifer {seed}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(bark, 0);
            mesh.SetTriangles(needles, 1);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// A finished tree object: two detail levels, a trunk collider, and wood to mine.
        public static GameObject Make(Mesh near, Mesh far, Material[] materials, float height)
        {
            var tree = new GameObject("Pine");
            GameObject Level(Mesh mesh, string name)
            {
                var level = new GameObject(name);
                level.transform.SetParent(tree.transform, false);
                level.AddComponent<MeshFilter>().sharedMesh = mesh;
                level.AddComponent<MeshRenderer>().sharedMaterials = materials;
                return level;
            }
            var lods = tree.AddComponent<LODGroup>();
            lods.SetLODs(new[]
            {
                new LOD(0.12f, new Renderer[] { Level(near, "Near").GetComponent<Renderer>() }),
                new LOD(0.012f, new Renderer[] { Level(far, "Far").GetComponent<Renderer>() }),
            });
            var trunk = tree.AddComponent<CapsuleCollider>();
            trunk.radius = height * 0.03f;
            trunk.height = height * 0.6f;
            trunk.center = new Vector3(0, height * 0.3f, 0);
            return tree;
        }
    }
}
