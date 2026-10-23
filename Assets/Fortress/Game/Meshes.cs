using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Game
{
    /// Meshes made in code.
    public static class Meshes
    {
        static readonly Dictionary<(Vector3, Vector3), Mesh> boxes = new();

        /// A box `size` metres, centred on its origin, textured at real scale: one texture repeat
        /// covers `TileMetres` on every face, so bricks stay brick-sized whatever the piece's shape.
        /// The texture is laid as if from `offset`, the box's position within its piece, so boxes
        /// built side by side (a wall's rows of bricks) carry the pattern on across the seams.
        public const float TileMetres = 1.5f;

        public static Mesh Box(Vector3 size, Vector3 offset = default)
        {
            if (boxes.TryGetValue((size, offset), out var cached)) return cached;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var half = size / 2;
            // Each face: its normal, and the two in-face axes the texture runs along (u across, v up).
            void Face(Vector3 normal, Vector3 u, Vector3 v)
            {
                var centre = Vector3.Scale(normal, half);
                int start = vertices.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    float su = corner == 1 || corner == 2 ? 1 : -1, sv = corner >= 2 ? 1 : -1;
                    var vertex = centre + Vector3.Scale(u, half) * su + Vector3.Scale(v, half) * sv;
                    vertices.Add(vertex);
                    normals.Add(normal);
                    uvs.Add(new Vector2(Vector3.Dot(vertex + offset, u), Vector3.Dot(vertex + offset, v)) / TileMetres);
                }
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
            Face(Vector3.forward, Vector3.left, Vector3.up);
            Face(Vector3.back, Vector3.right, Vector3.up);
            Face(Vector3.right, Vector3.forward, Vector3.up);
            Face(Vector3.left, Vector3.back, Vector3.up);
            Face(Vector3.up, Vector3.right, Vector3.forward);
            Face(Vector3.down, Vector3.right, Vector3.back);
            var mesh = new Mesh { name = $"Box {size}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            boxes[(size, offset)] = mesh;
            return mesh;
        }
    }
}
