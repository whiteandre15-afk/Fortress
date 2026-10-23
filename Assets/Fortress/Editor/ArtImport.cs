using UnityEditor;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// Import settings for the downloaded art, keyed off the file names fetch_assets.py writes.
    public class ArtImport : AssetPostprocessor
    {
        static bool IsArt(string path) => path.Contains("/Fortress/Art/");

        /// Plants drawn with alpha cut-outs.
        static bool IsFoliage(string path) => path.Contains("/Branch/") || path.Contains("/Fern/") || path.Contains("/GrassTuft/");

        void OnPreprocessTexture()
        {
            if (!IsArt(assetPath)) return;
            var texture = (TextureImporter)assetImporter;
            texture.anisoLevel = 8;
            if (assetPath.EndsWith(".hdr"))
            {
                texture.textureShape = TextureImporterShape.TextureCube;
                texture.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                texture.maxTextureSize = 2048;
                return;
            }
            texture.maxTextureSize = 2048;
            if (assetPath.EndsWith("_normal.png")) texture.textureType = TextureImporterType.NormalMap;
            else if (assetPath.EndsWith("_mask.png")) texture.sRGBTexture = false;
            if (IsFoliage(assetPath) && assetPath.EndsWith("_albedo.png"))
            {
                texture.alphaIsTransparency = true;
                // Keeps thin needles and blades from vanishing in the distance as mipmaps blur the alpha.
                texture.mipMapsPreserveCoverage = true;
                texture.alphaTestReferenceValue = 0.5f;
                texture.wrapMode = TextureWrapMode.Clamp;
            }
        }

        void OnPreprocessModel()
        {
            if (!IsArt(assetPath)) return;
            var model = (ModelImporter)assetImporter;
            // Readable so rocks can get mesh colliders at runtime.
            model.isReadable = true;
            model.importAnimation = false;
            model.importCameras = false;
            model.importLights = false;
        }
    }
}
