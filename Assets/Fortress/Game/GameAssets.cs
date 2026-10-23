using System.Collections.Generic;
using Fortress.Building;
using UnityEngine;

namespace Fortress.Game
{
    /// Materials, textures, meshes and sounds, made or loaded once and shared.
    public static class GameAssets
    {
        static Material lit, ghost, particle;
        static readonly Dictionary<BuildMaterial, Material> pieceMaterials = new();
        static readonly Dictionary<string, AudioClip> clips = new();

        /// The base URP Lit material that every solid surface is copied from.
        public static Material LitBase => lit ??= Resources.Load<Material>("Materials/Lit");
        /// See-through, unlit: the build ghost and the crate beams.
        public static Material GhostBase => ghost ??= Resources.Load<Material>("Materials/Ghost");
        public static Material ParticleBase => particle ??= Resources.Load<Material>("Materials/Particle");

        public static Material Lit(Color color, float smoothness = 0.2f, float metallic = 0)
        {
            var material = new Material(LitBase);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            material.enableInstancing = true;
            return material;
        }

        public static Material Lit(Texture2D texture, float smoothness = 0.2f, float metallic = 0)
        {
            var material = Lit(Color.white, smoothness, metallic);
            material.SetTexture("_BaseMap", texture);
            return material;
        }

        public static Material Ghost(Color color)
        {
            var material = new Material(GhostBase);
            material.SetColor("_BaseColor", color);
            return material;
        }

        /// A scanned surface made by the editor setup from the downloaded art, e.g. "Brick" or "Bark".
        public static Material Art(string name) => Resources.Load<Material>("Materials/Art/" + name);

        /// A scanned model made into a ready-to-place object by the editor setup, e.g. "Rifle".
        public static GameObject Model(string name) => Resources.Load<GameObject>("Models/" + name);

        /// The shared surface for every piece built from `material`.
        public static Material Piece(BuildMaterial material)
        {
            if (pieceMaterials.TryGetValue(material, out var cached)) return cached;
            var surface = Art(material switch
            {
                BuildMaterial.Wood => "Planks",
                BuildMaterial.Brick => "Brick",
                _ => "Metal",
            });
            pieceMaterials[material] = surface;
            return surface;
        }

        public static AudioClip Sound(string name)
        {
            if (!clips.TryGetValue(name, out var clip)) clips[name] = clip = Resources.Load<AudioClip>("Audio/" + name);
            return clip;
        }

        /// Loads a mesh exported from the iOS version as raw triangles: per vertex, position, normal
        /// and texture coordinate as 8 floats. The numbers are SceneKit's (right-handed, texture
        /// origin top-left), so it belongs under a z-mirrored parent; only the texture flips here.
        public static Mesh RawMesh(string name)
        {
            var bytes = Resources.Load<TextAsset>("Meshes/" + name).bytes;
            int count = bytes.Length / 32;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            var triangles = new int[count];
            for (int i = 0; i < count; i++)
            {
                float F(int component) => System.BitConverter.ToSingle(bytes, i * 32 + component * 4);
                vertices[i] = new Vector3(F(0), F(1), F(2));
                normals[i] = new Vector3(F(3), F(4), F(5));
                uvs[i] = new Vector2(F(6), 1 - F(7));
                triangles[i] = i;
            }
            var mesh = new Mesh { name = name, vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh cylinder, sphere;
        public static Mesh Cylinder => cylinder ??= PrimitiveMesh(PrimitiveType.Cylinder);
        public static Mesh Sphere => sphere ??= PrimitiveMesh(PrimitiveType.Sphere);

        static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var probe = GameObject.CreatePrimitive(type);
            var mesh = probe.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(probe);
            return mesh;
        }
    }
}
